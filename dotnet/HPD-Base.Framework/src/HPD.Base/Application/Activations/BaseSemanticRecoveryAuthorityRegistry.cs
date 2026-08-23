using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

/// <summary>Owns graph-finalized semantic restore selections and external recovery instances.</summary>
public sealed class BaseSemanticRecoveryAuthorityRegistry : IAsyncDisposable
{
    private readonly Dictionary<string, OwnedAuthority> _authorities;

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
                _authorities.Add(store, new OwnedAuthority(registration, instance));
            }
            else if (registration is not null)
                throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
        }
        if (byStore.Any(group => !Selections.ContainsKey(group.Key)))
            throw new InvalidOperationException(BaseSemanticActivationErrorCodes.Invalid);
    }

    /// <summary>Gets the immutable selected restore authority by logical store.</summary>
    public ImmutableDictionary<string, BaseSemanticActivationRestoreSelection> Selections { get; }

    internal (BaseSemanticRecoveryAuthorityDefinition Definition, IBaseSemanticActivationRecoveryAuthority Instance)? Find(string logicalStoreId) =>
        _authorities.TryGetValue(logicalStoreId, out OwnedAuthority? value) ? (value.Registration.Definition, value.Instance) : null;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (OwnedAuthority authority in _authorities.Values)
            if (authority.Instance is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (authority.Instance is IDisposable disposable) disposable.Dispose();
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

    private sealed record OwnedAuthority(BaseSemanticRecoveryAuthorityRegistration Registration, IBaseSemanticActivationRecoveryAuthority Instance);
}
