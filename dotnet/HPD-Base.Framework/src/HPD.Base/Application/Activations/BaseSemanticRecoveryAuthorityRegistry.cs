using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Owns graph-finalized semantic restore selections and external recovery instances.</summary>
public sealed class BaseSemanticRecoveryAuthorityRegistry : IAsyncDisposable
{
    private readonly Dictionary<string, OwnedAuthority> _authorities;
    private int _disposing;

    internal BaseSemanticRecoveryAuthorityRegistry(
        IEnumerable<BaseSemanticActivationRestoreSelection> selections,
        IEnumerable<BaseSemanticRecoveryAuthorityRegistration> registrations,
        BaseSemanticActivationCapability providerCapability,
        int installedSemanticDefinitionCount,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        Selections = selections.Select(SealSelection).ToImmutableDictionary(static value => value.LogicalStoreId, StringComparer.Ordinal);
        if (Selections.Count != selections.Count()) throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        if (installedSemanticDefinitionCount < 0 || installedSemanticDefinitionCount == 0 && Selections.Count != 0
            || installedSemanticDefinitionCount > 0 && Selections.Count != 1)
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        var byStore = registrations.GroupBy(static value => value.Definition.LogicalStoreId, StringComparer.Ordinal).ToArray();
        if (byStore.Any(static group => group.Count() != 1)) throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        _authorities = [];
        foreach ((string store, BaseSemanticActivationRestoreSelection selection) in Selections)
        {
            if (selection.EnabledRestoreMode is { } enabled && !providerCapability.RestoreModes.Contains(enabled))
                throw new InvalidOperationException(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
            BaseSemanticRecoveryAuthorityRegistration? registration = byStore.SingleOrDefault(group => group.Key == store)?.Single();
            if (selection.EnabledRestoreMode == BaseActivationRestoreMode.NewDisasterDomain)
            {
                if (registration is null || !BaseSemanticRecoveryAuthorityContract.IsValidAt(registration, timeProvider.GetUtcNow()))
                    throw new InvalidOperationException(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
                IBaseSemanticActivationRecoveryAuthority instance = registration.Factory.CreateOwned()
                    ?? throw new InvalidOperationException(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
                bool matches;
                try { matches = instance.Descriptor is { } descriptor && InstanceMatches(registration, descriptor); }
                catch
                {
                    DisposeOwnedSuppressingFailure(instance);
                    throw new InvalidOperationException(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
                }
                if (!matches)
                {
                    DisposeOwnedSuppressingFailure(instance);
                    throw new InvalidOperationException(BaseSemanticActivationErrorCodes.CapabilityUnavailable);
                }
                _authorities.Add(store, new OwnedAuthority(registration, instance,
                    new SemaphoreSlim(registration.Definition.Limits.MaximumConcurrentOperations)));
            }
            else if (registration is not null)
                throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        }
        if (byStore.Any(group => !Selections.ContainsKey(group.Key)))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
    }

    /// <summary>Gets the immutable selected restore authority by logical store.</summary>
    public ImmutableDictionary<string, BaseSemanticActivationRestoreSelection> Selections { get; }
    internal bool HasExternalAuthority(string logicalStoreId) => _authorities.ContainsKey(logicalStoreId);
    internal bool HasOperationalDependency(string logicalStoreId)
    {
        if (!_authorities.TryGetValue(logicalStoreId, out OwnedAuthority? authority)) return false;
        lock (authority.Sync) return authority.ActiveCalls != 0 || authority.RetainedLateWork != 0 || authority.IsQuarantined;
    }

    internal (BaseSemanticRecoveryAuthorityDefinition Definition, IBaseSemanticActivationRecoveryAuthority Instance)? Find(string logicalStoreId) =>
        _authorities.TryGetValue(logicalStoreId, out OwnedAuthority? value) ? (value.Registration.Definition, value.Instance) : null;

    internal async ValueTask<BaseResult<T>> InvokeAsync<T>(string logicalStoreId, TimeSpan timeout,
        Func<IBaseSemanticActivationRecoveryAuthority, CancellationToken, ValueTask<BaseResult<T>>> operation,
        CancellationToken cancellationToken)
    {
        if (!_authorities.TryGetValue(logicalStoreId, out OwnedAuthority? authority))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.ExternalPublicationUnavailable);
        lock (authority.Sync)
        {
            if (Volatile.Read(ref _disposing) != 0 || authority.IsQuarantined || authority.DisposeWhenDrained)
                throw new InvalidOperationException(BaseSemanticActivationErrorCodes.ExternalPublicationUnavailable);
            authority.ActiveCalls++;
        }
        using var acquisition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acquisition.CancelAfter(timeout);
        try { await authority.Slots.WaitAsync(acquisition.Token).ConfigureAwait(false); }
        catch { CompleteCall(authority); throw; }
        var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationLifetime.CancelAfter(timeout);
        Task<BaseResult<T>> task;
        try { task = operation(authority.Instance, operationLifetime.Token).AsTask(); }
        catch { operationLifetime.Dispose(); authority.Slots.Release(); CompleteCall(authority); throw; }
        try
        {
            BaseResult<T> result = await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            operationLifetime.Dispose(); authority.Slots.Release(); CompleteCall(authority); return result;
        }
        catch when (!task.IsCompleted)
        {
            lock (authority.Sync)
            {
                authority.RetainedLateWork++;
                authority.IsQuarantined = true;
            }
            _ = task.ContinueWith(static (_, state) =>
            {
                var retained = ((OwnedAuthority Authority, CancellationTokenSource Lifetime))state!;
                retained.Lifetime.Dispose(); retained.Authority.Slots.Release();
                lock (retained.Authority.Sync) retained.Authority.RetainedLateWork--;
                CompleteCall(retained.Authority);
            }, (authority, operationLifetime), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
        catch { operationLifetime.Dispose(); authority.Slots.Release(); CompleteCall(authority); throw; }
    }

    internal async ValueTask<BaseResult<TResult>> InvokeAsync<TRequest, TResult>(string logicalStoreId,
        TimeSpan timeout, TRequest request, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResult> resultType,
        Func<IBaseSemanticActivationRecoveryAuthority, TRequest, CancellationToken, ValueTask<BaseResult<TResult>>> operation,
        CancellationToken cancellationToken)
    {
        BaseSemanticRecoveryOperationLimits limits = _authorities.TryGetValue(logicalStoreId, out OwnedAuthority? owned)
            ? owned.Registration.Definition.Limits
            : throw new InvalidOperationException(BaseSemanticActivationErrorCodes.ExternalPublicationUnavailable);
        byte[] requestBytes = JsonSerializer.SerializeToUtf8Bytes(request, requestType);
        if (requestBytes.LongLength > limits.MaximumRequestBytes || requestBytes.LongLength > limits.MaximumTransientBytes)
            throw new InvalidOperationException(BaseSubjectErrorCodes.BudgetExceeded);
        BaseResult<TResult> result = await InvokeAsync(logicalStoreId, timeout,
            (instance, token) => operation(instance, request, token), cancellationToken).ConfigureAwait(false);
        if (result is BaseSuccess<TResult> success)
        {
            byte[] resultBytes = JsonSerializer.SerializeToUtf8Bytes(success.Value, resultType);
            if (resultBytes.LongLength > limits.MaximumResultBytes
                || checked(requestBytes.LongLength + resultBytes.LongLength) > limits.MaximumTransientBytes)
                throw new InvalidOperationException(BaseSubjectErrorCodes.BudgetExceeded);
        }
        else if (result is BaseFailure<TResult> failure)
        {
            long resultBytes = checked(32L
                + System.Text.Encoding.UTF8.GetByteCount(failure.Error.Code)
                + System.Text.Encoding.UTF8.GetByteCount(failure.Error.Message)
                + (failure.Error.Detail is null ? 1 : 5L + System.Text.Encoding.UTF8.GetByteCount(failure.Error.Detail)));
            if (resultBytes > limits.MaximumResultBytes
                || checked(requestBytes.LongLength + resultBytes) > limits.MaximumTransientBytes)
                throw new InvalidOperationException(BaseSubjectErrorCodes.BudgetExceeded);
        }
        return result;
    }

    internal bool IsQuarantined(string logicalStoreId)
    {
        if (!_authorities.TryGetValue(logicalStoreId, out OwnedAuthority? value)) return false;
        lock (value.Sync) return value.IsQuarantined;
    }

    /// <summary>Explicitly releases quarantine after every retained late operation has completed.</summary>
    internal BaseResult<BaseSemanticRecoveryQuarantineRecoveryResult> RecoverQuarantine(
        BaseSemanticRecoveryQuarantineRecoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Identity);
        if (!_authorities.TryGetValue(request.LogicalStoreId, out OwnedAuthority? authority))
            return new BaseFailure<BaseSemanticRecoveryQuarantineRecoveryResult>(OperationStatus.NotFound,
                new BaseError { Code = BaseSemanticActivationErrorCodes.ExternalPublicationUnavailable,
                    Message = "Semantic recovery authority is unavailable.", Category = ErrorCategory.NotFound }, null, null);
        lock (authority.Sync)
        {
            if (authority.RetainedLateWork != 0)
                return new BaseSuccess<BaseSemanticRecoveryQuarantineRecoveryResult>(new()
                { Released = false, RetainedLateWork = authority.RetainedLateWork }, OperationStatus.Ok, null, null, null, null);
            authority.IsQuarantined = false;
            return new BaseSuccess<BaseSemanticRecoveryQuarantineRecoveryResult>(new()
            { Released = true, RetainedLateWork = 0 }, OperationStatus.Updated, null, null, null, null);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0) return;
        foreach (OwnedAuthority authority in _authorities.Values)
        {
            lock (authority.Sync)
            {
                authority.DisposeWhenDrained = true;
                if (authority.ActiveCalls != 0) continue;
            }
            if (Interlocked.Exchange(ref authority.DisposeStarted, 1) != 0) continue;
            if (authority.Instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (authority.Instance is IDisposable disposable) disposable.Dispose();
        }
    }

    internal void DisposeAfterFailedPublication()
    {
        foreach (OwnedAuthority authority in _authorities.Values)
            if (authority.Instance is IDisposable disposable) disposable.Dispose();
            else if (authority.Instance is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void DisposeOwnedSuppressingFailure(IBaseSemanticActivationRecoveryAuthority instance)
    {
        try
        {
            if (instance is IDisposable disposable) disposable.Dispose();
            else if (instance is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            // The malformed external instance is never published. Disposal failure must not
            // replace the stable capability failure exposed by graph finalization.
        }
    }

    private static void DisposeAuthorityOnce(OwnedAuthority authority)
    {
        if (Interlocked.Exchange(ref authority.DisposeStarted, 1) == 0)
            DisposeOwnedSuppressingFailure(authority.Instance);
    }

    private static void CompleteCall(OwnedAuthority authority)
    {
        bool dispose;
        lock (authority.Sync)
        {
            authority.ActiveCalls--;
            dispose = authority.ActiveCalls == 0 && authority.DisposeWhenDrained;
        }
        if (dispose) DisposeAuthorityOnce(authority);
    }

    private static BaseSemanticActivationRestoreSelection SealSelection(BaseSemanticActivationRestoreSelection value)
    {
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(value.Identity);
        if (string.IsNullOrWhiteSpace(value.LogicalStoreId) || value.SelectionGeneration <= 0
            || value.EnabledRestoreMode is { } mode && !Enum.IsDefined(mode))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        ImmutableArray<byte> checksum = SelectionChecksum(value);
        if (!value.Checksum.IsDefaultOrEmpty && (value.Checksum.Length != 32
            || !CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), checksum.AsSpan())))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        return value with
        {
            LogicalStoreId = new string(value.LogicalStoreId.AsSpan()),
            Identity = value.Identity with { Fingerprint = BaseMutationRequestFingerprint.Create(value.Identity.Fingerprint.ToArray()) },
            Checksum = checksum,
        };
    }

    /// <summary>Computes the canonical restore-selection checksum.</summary>
    public static ImmutableArray<byte> SelectionChecksum(BaseSemanticActivationRestoreSelection value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.restoreSelection.v1\0"u8);
        Add(System.Text.Encoding.UTF8.GetBytes(value.LogicalStoreId)); Add([(byte)(value.EnabledRestoreMode is null ? 0 : 1)]);
        AddInt(value.EnabledRestoreMode is null ? 0 : (int)value.EnabledRestoreMode.Value); AddLong(value.SelectionGeneration);
        Add(System.Text.Encoding.UTF8.GetBytes(value.Identity.Scope)); Add(System.Text.Encoding.UTF8.GetBytes(value.Identity.Operation));
        Add(System.Text.Encoding.UTF8.GetBytes(value.Identity.IdempotencyKey)); Add(value.Identity.Fingerprint.ToArray());
        return hash.GetHashAndReset().ToImmutableArray();
        void Add(ReadOnlySpan<byte> bytes) { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes); }
        void AddInt(int number) { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, number); hash.AppendData(bytes); }
        void AddLong(long number) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, number); hash.AppendData(bytes); }
    }

    private static bool InstanceMatches(BaseSemanticRecoveryAuthorityRegistration registration,
        BaseSemanticRecoveryAuthorityInstanceDescriptor value)
    {
        BaseSemanticRecoveryAuthorityCertificationReceipt certification = registration.Certification;
        BaseSemanticRecoveryAuthorityDefinition definition = registration.Definition;
        return value.ImplementationContractId == certification.ImplementationContractId
            && value.ImplementationContractVersion == certification.ImplementationContractVersion
            && Fixed(value.CapabilityChecksum, certification.CapabilityChecksum)
            && Fixed(value.KeyAuthorityChecksum, definition.KeyAuthority.Checksum)
            && Fixed(value.DefinitionChecksum, definition.ContractChecksum)
            && Fixed(value.CertificationChecksum, certification.Checksum)
            && Fixed(value.Checksum, BaseSemanticRecoveryAuthorityContract.InstanceDescriptorChecksum(value));
    }

    private static bool Fixed(ImmutableArray<byte> left, ImmutableArray<byte> right) => left.Length == 32
        && right.Length == 32 && CryptographicOperations.FixedTimeEquals(left.AsSpan(), right.AsSpan());

    private sealed class OwnedAuthority(
        BaseSemanticRecoveryAuthorityRegistration registration,
        IBaseSemanticActivationRecoveryAuthority instance,
        SemaphoreSlim slots)
    {
        internal BaseSemanticRecoveryAuthorityRegistration Registration { get; } = registration;
        internal IBaseSemanticActivationRecoveryAuthority Instance { get; } = instance;
        internal SemaphoreSlim Slots { get; } = slots;
        internal object Sync { get; } = new();
        internal volatile bool IsQuarantined;
        internal long RetainedLateWork;
        internal long ActiveCalls;
        internal volatile bool DisposeWhenDrained;
        internal int DisposeStarted;
    }
}
