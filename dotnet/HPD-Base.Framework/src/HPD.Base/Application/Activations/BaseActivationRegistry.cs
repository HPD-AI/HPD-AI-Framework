using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Defines deterministic retry behavior for one activation definition.</summary>
public sealed record BaseActivationRetryProfile
{
    /// <summary>Gets the positive maximum number of attempts.</summary>
    public required int MaximumAttempts { get; init; }
    /// <summary>Gets the initial retry delay in milliseconds.</summary>
    public required long InitialDelayMilliseconds { get; init; }
    /// <summary>Gets the bounded maximum retry delay in milliseconds.</summary>
    public required long MaximumDelayMilliseconds { get; init; }
    /// <summary>Gets the fixed-point multiplier numerator.</summary>
    public required int MultiplierNumerator { get; init; }
    /// <summary>Gets the positive fixed-point multiplier denominator.</summary>
    public required int MultiplierDenominator { get; init; }
    /// <summary>Gets the deterministic jitter basis points.</summary>
    public required int JitterBasisPoints { get; init; }
    /// <summary>Gets the closed retryable stable failure-code allowlist.</summary>
    public required ImmutableArray<string> RetryableFailureCodes { get; init; }
}

/// <summary>Defines one installed activation's complete effective maxima.</summary>
public sealed record BaseActivationLimits
{
    /// <summary>Gets the maximum canonical input bytes.</summary>
    public required long MaximumInputBytes { get; init; }
    /// <summary>Gets the maximum canonical result bytes.</summary>
    public required long MaximumResultBytes { get; init; }
    /// <summary>Gets the maximum attempts.</summary>
    public required int MaximumAttempts { get; init; }
    /// <summary>Gets the maximum renewals per attempt.</summary>
    public required int MaximumRenewalsPerAttempt { get; init; }
    /// <summary>Gets the maximum guarded children per attempt.</summary>
    public required int MaximumChildrenPerAttempt { get; init; }
    /// <summary>Gets the maximum lineage depth.</summary>
    public required int MaximumLineageDepth { get; init; }
    /// <summary>Gets the default lease duration.</summary>
    public required TimeSpan LeaseDuration { get; init; }
    /// <summary>Gets the handler execution deadline.</summary>
    public required TimeSpan HandlerTimeout { get; init; }
    /// <summary>Gets provider-operation limits.</summary>
    public required BaseActivationExecutionLimits Provider { get; init; }
    /// <summary>Gets the exact shared atomic-creation safety envelope.</summary>
    public required BaseAtomicMutationExecutionLimits AtomicCreation { get; init; }
}

/// <summary>Binds an activation definition to one graph-owned handler factory.</summary>
public sealed record BaseActivationHandlerBinding
{
    /// <summary>Gets the stable handler identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive handler version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the stable factory identity.</summary>
    public required string FactoryId { get; init; }
    /// <summary>Gets the L41 input graph-node identity.</summary>
    public required string InputTypeId { get; init; }
    /// <summary>Gets the L41 result graph-node identity.</summary>
    public required string ResultTypeId { get; init; }
    /// <summary>Gets the exact worker subject kind.</summary>
    public required AccessSubjectKind WorkerSubjectKind { get; init; }
    /// <summary>Gets the Runtime-owned canonical checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Defines one graph-installed durable activation.</summary>
public sealed record BaseActivationGrantSet
{
    /// <summary>Gets the exact enqueue grant identity.</summary>
    public required string Enqueue { get; init; }
    /// <summary>Gets the exact due-observation grant identity.</summary>
    public required string Observe { get; init; }
    /// <summary>Gets the exact claim grant identity.</summary>
    public required string Claim { get; init; }
    /// <summary>Gets the exact handler-execution grant identity.</summary>
    public required string Execute { get; init; }
    /// <summary>Gets the exact lease-renewal grant identity.</summary>
    public required string Renew { get; init; }
    /// <summary>Gets the exact completion grant identity.</summary>
    public required string Complete { get; init; }
    /// <summary>Gets the exact failed-attempt grant identity.</summary>
    public required string Fail { get; init; }
    /// <summary>Gets the exact cancellation grant identity.</summary>
    public required string Cancel { get; init; }
    /// <summary>Gets the exact inspection grant identity.</summary>
    public required string Inspect { get; init; }
    /// <summary>Gets the exact receipt-replay grant identity.</summary>
    public required string Replay { get; init; }
    /// <summary>Gets the exact migration grant identity.</summary>
    public required string Migrate { get; init; }
    /// <summary>Gets the exact unknown-effect reconciliation grant identity.</summary>
    public required string Reconcile { get; init; }
    /// <summary>Gets the exact exhausted-activation retry grant identity.</summary>
    public required string Retry { get; init; }
    /// <summary>Gets the exact terminal-disposal grant identity.</summary>
    public required string Dispose { get; init; }
    /// <summary>Gets the exact definition-removal grant identity.</summary>
    public required string Remove { get; init; }
    /// <summary>Gets the exact repair grant identity.</summary>
    public required string Repair { get; init; }
}

/// <summary>Defines one graph-installed durable activation.</summary>
public sealed record BaseActivationDefinition
{
    /// <summary>Gets the stable activation-definition identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the execution class.</summary>
    public required BaseActivationExecutionClass ExecutionClass { get; init; }
    /// <summary>Gets the L41 input graph-node identity.</summary>
    public required string InputTypeId { get; init; }
    /// <summary>Gets the L41 result graph-node identity.</summary>
    public required string ResultTypeId { get; init; }
    /// <summary>Gets the complete closed operation-grant authority.</summary>
    public required BaseActivationGrantSet Grants { get; init; }
    /// <summary>Gets exact declared source-grant identities.</summary>
    public required ImmutableArray<string> SourceGrantIds { get; init; }
    /// <summary>Gets deterministic retry policy.</summary>
    public required BaseActivationRetryProfile Retry { get; init; }
    /// <summary>Gets effective definition limits.</summary>
    public required BaseActivationLimits Limits { get; init; }
    /// <summary>Gets the worker handler binding, forbidden for transactional operations.</summary>
    public BaseActivationHandlerBinding? Handler { get; init; }
    /// <summary>Gets the closed installed target for a transactional activation.</summary>
    public BaseTransactionalActivationTarget? TransactionalTarget { get; init; }
    /// <summary>Gets the Runtime-owned canonical checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Identifies one closed BASE operation executable as a transactional activation.</summary>
public abstract record BaseTransactionalActivationTarget
{
    private protected BaseTransactionalActivationTarget() { }
}

/// <summary>Targets one exact installed selection-mutation profile.</summary>
public sealed record BaseSelectionMutationActivationTarget : BaseTransactionalActivationTarget
{
    /// <summary>Gets the stable profile identity.</summary>
    public required string ProfileId { get; init; }
    /// <summary>Gets the positive profile version.</summary>
    public required int ProfileVersion { get; init; }
    /// <summary>Gets the exact installed profile checksum.</summary>
    public required string ProfileChecksum { get; init; }
}

/// <summary>Targets one exact installed registered module mutation.</summary>
public sealed record BaseModuleMutationActivationTarget : BaseTransactionalActivationTarget
{
    /// <summary>Gets the stable operation identity.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the positive operation version.</summary>
    public required int OperationVersion { get; init; }
    /// <summary>Gets the exact installed operation checksum.</summary>
    public required string OperationChecksum { get; init; }
}

/// <summary>Executes one graph-owned activation handler.</summary>
public interface IBaseActivationHandler<TInput, TResult>
{
    /// <summary>Executes under one current, fenced activation context.</summary>
    ValueTask<BaseActivationHandlerResult<TResult>> ExecuteAsync(
        BaseActivationContext context,
        TInput input,
        CancellationToken cancellationToken);
}

/// <summary>Exposes only fenced, installed capabilities to one activation handler.</summary>
public sealed class BaseActivationContext
{
    private readonly int _maximumChildren;
    private readonly HashSet<(string StepId, int Ordinal)> _children = [];

    internal BaseActivationContext(
        BaseActivationDefinitionKey definition,
        BaseActivationClaimAuthority claim,
        BaseActivationLeaseObservation lease,
        CancellationToken cancellationToken,
        int maximumChildren)
    {
        Definition = definition;
        Claim = claim;
        Lease = lease;
        CancellationToken = cancellationToken;
        _maximumChildren = maximumChildren;
    }

    /// <summary>Gets the exact installed definition.</summary>
    public BaseActivationDefinitionKey Definition { get; }
    /// <summary>Gets stable current claim authority.</summary>
    public BaseActivationClaimAuthority Claim { get; }
    /// <summary>Gets the current immutable lease observation.</summary>
    public BaseActivationLeaseObservation Lease { get; internal set; }
    /// <summary>Gets the cancellation signal for cooperative handler work.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Derives one deterministic child request identity without minting a fence.</summary>
    public BaseMutationRequestIdentity DeriveChildIdentity(string stepId, int childOrdinal, BaseMutationRequestFingerprint fingerprint)
    {
        BaseApplicationId.Validate(stepId, nameof(stepId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(childOrdinal);
        return BaseMutationRequestIdentity.Create(
            $"activation:{Claim.ActivationId}", stepId, childOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture), fingerprint);
    }

    /// <summary>Creates L50 execution options fenced to this exact live claim.</summary>
    public BaseModuleMutationExecutionOptions GuardModuleMutation(
        string stepId,
        int childOrdinal,
        BaseMutationRequestFingerprint fingerprint,
        BaseModuleMutationExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        BaseApplicationId.Validate(stepId, nameof(stepId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(childOrdinal);
        lock (_children)
        {
            bool added = _children.Add((stepId, childOrdinal));
            if (added && _children.Count > _maximumChildren)
            {
                _children.Remove((stepId, childOrdinal));
                throw new InvalidOperationException("base.activation.childLimitExceeded");
            }
        }
        return (options ?? new BaseModuleMutationExecutionOptions()) with
        {
            ActivationGuard = new BaseActivationGuard
            {
                Claim = Claim,
                StepId = new string(stepId.AsSpan()),
                ChildOrdinal = childOrdinal,
                ChildRequestFingerprint = fingerprint.ToArray().ToImmutableArray(),
            },
        };
    }
}

/// <summary>Contains the closed result returned by a worker handler.</summary>
public sealed record BaseActivationHandlerResult<TResult>
{
    /// <summary>Gets the successful result when completion was selected.</summary>
    public TResult? Result { get; init; }
    /// <summary>Gets the stable safe failure code when failure was selected.</summary>
    public string? FailureCode { get; init; }
    /// <summary>Gets whether the failure may enter deterministic retry.</summary>
    public bool Retryable { get; init; }
}

/// <summary>Contains an inert source-generated activation registration identity.</summary>
public sealed class BaseActivationRegistrationIdentity<TInput, TResult> : IBaseSerializerMetadataSource
{
    /// <summary>Initializes an inert registration identity.</summary>
    public BaseActivationRegistrationIdentity(
        string id,
        int version,
        ReadOnlyMemory<byte> checksum,
        JsonTypeInfo<TInput> input,
        JsonTypeInfo<TResult> result)
    {
        Id = new string(id.AsSpan());
        Version = version;
        Checksum = checksum.ToArray();
        Input = input;
        Result = result;
    }

    /// <summary>Gets the definition identity.</summary>
    public string Id { get; }
    /// <summary>Gets the definition version.</summary>
    public int Version { get; }
    /// <summary>Gets the canonical definition checksum.</summary>
    public ReadOnlyMemory<byte> Checksum { get; }
    /// <summary>Gets source-generated input metadata.</summary>
    public JsonTypeInfo<TInput> Input { get; }
    /// <summary>Gets source-generated result metadata.</summary>
    public JsonTypeInfo<TResult> Result { get; }
    IReadOnlyList<System.Text.Json.Serialization.Metadata.JsonTypeInfo> IBaseSerializerMetadataSource.Roots => [Input, Result];
    bool IBaseSerializerMetadataSource.Generated => false;
    BaseSerializerContextRegistration? IBaseSerializerMetadataSource.Registration => null;
    IReadOnlyList<Type> IBaseSerializerMetadataSource.RootTypes => [typeof(TInput), typeof(TResult)];
    IReadOnlyList<BaseSerializerPropertyDeclaration>? IBaseSerializerMetadataSource.SerializerDeclarations => null;
    CollectionDefinition? IBaseSerializerMetadataSource.CollectionDefinition => null;
    void IBaseSerializerMetadataSource.Bind(BaseSerializerMetadataOwner owner) { }
}

/// <summary>Registers one graph-owned activation handler and its closed codecs.</summary>
public sealed record BaseActivationHandlerRegistration<TInput, TResult>
{
    /// <summary>Gets the sealed activation definition.</summary>
    public required BaseActivationDefinition Definition { get; init; }
    /// <summary>Gets the inert generated identity.</summary>
    public required BaseActivationRegistrationIdentity<TInput, TResult> Identity { get; init; }
    /// <summary>Gets the graph-owned Native-AOT-safe handler factory.</summary>
    public required Func<IServiceProvider, IBaseActivationHandler<TInput, TResult>> Factory { get; init; }
}

/// <summary>Registers one handler-free transactional activation and its closed codecs.</summary>
public sealed record BaseTransactionalActivationRegistration<TInput, TResult>
{
    /// <summary>Gets the sealed activation definition.</summary>
    public required BaseActivationDefinition Definition { get; init; }
    /// <summary>Gets the inert generated identity.</summary>
    public required BaseActivationRegistrationIdentity<TInput, TResult> Identity { get; init; }
}

/// <summary>Builds one sealed activation registration from closed graph-owned inputs.</summary>
public static class BaseActivationDefinitionBuilder
{
    /// <summary>Computes canonical authority and returns one inert registration.</summary>
    public static BaseActivationHandlerRegistration<TInput, TResult> Create<TInput, TResult>(
        BaseActivationDefinition definition,
        JsonTypeInfo<TInput> input,
        JsonTypeInfo<TResult> result,
        Func<IServiceProvider, IBaseActivationHandler<TInput, TResult>> factory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(factory);
        BaseActivationDefinition sealedDefinition = BaseActivationContract.Seal(definition);
        return new BaseActivationHandlerRegistration<TInput, TResult>
        {
            Definition = sealedDefinition,
            Identity = new BaseActivationRegistrationIdentity<TInput, TResult>(
                sealedDefinition.Id, sealedDefinition.Version, sealedDefinition.Checksum.ToArray(), input, result),
            Factory = factory,
        };
    }

    /// <summary>Computes canonical authority for one handler-free transactional activation.</summary>
    public static BaseTransactionalActivationRegistration<TInput, TResult> CreateTransactional<TInput, TResult>(
        BaseActivationDefinition definition,
        JsonTypeInfo<TInput> input,
        JsonTypeInfo<TResult> result)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        BaseActivationDefinition sealedDefinition = BaseActivationContract.Seal(definition);
        return new BaseTransactionalActivationRegistration<TInput, TResult>
        {
            Definition = sealedDefinition,
            Identity = new BaseActivationRegistrationIdentity<TInput, TResult>(
                sealedDefinition.Id, sealedDefinition.Version, sealedDefinition.Checksum.ToArray(), input, result),
        };
    }
}

internal interface IBaseActivationRegistration
{
    BaseActivationDefinition Definition { get; }
    object Identity { get; }
    object? CreateHandler(IServiceProvider services);
}

internal sealed class BaseInstalledTransactionalActivationRegistration<TInput, TResult>(
    BaseTransactionalActivationRegistration<TInput, TResult> registration) : IBaseActivationRegistration
{
    public BaseActivationDefinition Definition { get; } = BaseActivationContract.Seal(registration.Definition);
    public object Identity { get; } = registration.Identity;
    public object? CreateHandler(IServiceProvider services) => null;
}

internal sealed class BaseActivationRegistration<TInput, TResult>(
    BaseActivationHandlerRegistration<TInput, TResult> registration) : IBaseActivationRegistration
{
    public BaseActivationDefinition Definition { get; } = BaseActivationContract.Seal(registration.Definition);
    public object Identity { get; } = registration.Identity;
    public object CreateHandler(IServiceProvider services) => registration.Factory(services);
}

/// <summary>Provides immutable lookup over one finalized activation-definition owner.</summary>
public sealed class BaseActivationRegistry
{
    private readonly Dictionary<(string Id, int Version), IBaseActivationRegistration> _registrations;

    internal BaseActivationRegistry(IEnumerable<IBaseActivationRegistration> registrations)
    {
        _registrations = registrations.ToDictionary(
            static item => (item.Definition.Id, item.Definition.Version),
            static item => item);
    }

    /// <summary>Finds one exact installed activation definition.</summary>
    public BaseActivationDefinition? Find(string id, int version) =>
        _registrations.TryGetValue((id, version), out IBaseActivationRegistration? value)
            ? BaseActivationContract.Seal(value.Definition)
            : null;

    internal IBaseActivationRegistration? Registration(string id, int version) =>
        _registrations.GetValueOrDefault((id, version));

    internal IReadOnlyList<BaseActivationDefinition> Definitions => _registrations.Values
        .Select(static registration => registration.Definition)
        .OrderBy(static definition => definition.Id, StringComparer.Ordinal)
        .ThenBy(static definition => definition.Version)
        .ToArray();
}

internal static class BaseActivationContract
{
    internal static BaseActivationDefinition Seal(BaseActivationDefinition source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Validate(source);
        BaseActivationDefinition normalized = source with
        {
            Id = new string(source.Id.AsSpan()),
            OwningModuleId = new string(source.OwningModuleId.AsSpan()),
            InputTypeId = new string(source.InputTypeId.AsSpan()),
            ResultTypeId = new string(source.ResultTypeId.AsSpan()),
            Grants = CloneGrants(source.Grants),
            SourceGrantIds = source.SourceGrantIds.Order(StringComparer.Ordinal).Select(static value => new string(value.AsSpan())).ToImmutableArray(),
            Retry = source.Retry with
            {
                RetryableFailureCodes = source.Retry.RetryableFailureCodes.Order(StringComparer.Ordinal)
                    .Select(static value => new string(value.AsSpan())).ToImmutableArray(),
            },
            Limits = source.Limits with { Provider = source.Limits.Provider with { }, AtomicCreation = source.Limits.AtomicCreation with { Deadlines = source.Limits.AtomicCreation.Deadlines with { } } },
            Handler = source.Handler is null ? null : source.Handler with { Checksum = source.Handler.Checksum.ToArray().ToImmutableArray() },
            TransactionalTarget = source.TransactionalTarget switch
            {
                BaseSelectionMutationActivationTarget value => value with
                {
                    ProfileId = new string(value.ProfileId.AsSpan()),
                    ProfileChecksum = new string(value.ProfileChecksum.AsSpan()),
                },
                BaseModuleMutationActivationTarget value => value with
                {
                    OperationId = new string(value.OperationId.AsSpan()),
                    OperationChecksum = new string(value.OperationChecksum.AsSpan()),
                },
                null => null,
                _ => throw new InvalidOperationException("base.activation.definitionInvalid"),
            },
            Checksum = ImmutableArray<byte>.Empty,
        };
        return normalized with { Checksum = ComputeChecksum(normalized).ToImmutableArray() };
    }

    internal static void ValidateInstalled(BaseActivationDefinition source)
    {
        BaseActivationDefinition sealedDefinition = Seal(source);
        if (source.Checksum.Length != 32 || !CryptographicOperations.FixedTimeEquals(source.Checksum.AsSpan(), sealedDefinition.Checksum.AsSpan()))
            throw new InvalidOperationException("base.activation.definitionInvalid");
    }

    private static void Validate(BaseActivationDefinition value)
    {
        BaseApplicationId.Validate(value.Id, nameof(value.Id));
        BaseApplicationId.Validate(value.OwningModuleId, nameof(value.OwningModuleId));
        ValidateGrants(value.Grants);
        if (value.Version <= 0 || string.IsNullOrWhiteSpace(value.InputTypeId) || string.IsNullOrWhiteSpace(value.ResultTypeId))
            throw new InvalidOperationException("base.activation.definitionInvalid");
        if (value.ExecutionClass == BaseActivationExecutionClass.TransactionalOperation
            ? value.TransactionalTarget is null || value.Handler is not null
            : value.TransactionalTarget is not null || value.Handler is null)
            throw new InvalidOperationException("base.activation.definitionInvalid");
        ValidateTarget(value.TransactionalTarget);
        if (value.Retry.MaximumAttempts is < 1 or > 1024 || value.Limits.MaximumAttempts != value.Retry.MaximumAttempts ||
            value.Limits.MaximumInputBytes is < 1 or > 4L * 1024 * 1024 || value.Limits.MaximumResultBytes is < 1 or > 4L * 1024 * 1024 ||
            value.Limits.MaximumRenewalsPerAttempt is < 1 or > 4096 || value.Limits.MaximumChildrenPerAttempt is < 1 or > 4096 ||
            value.Limits.HandlerTimeout <= TimeSpan.Zero || value.Limits.HandlerTimeout > TimeSpan.FromHours(24) ||
            value.Retry.InitialDelayMilliseconds < 0 || value.Retry.MaximumDelayMilliseconds < value.Retry.InitialDelayMilliseconds ||
            value.Retry.MultiplierNumerator <= 0 || value.Retry.MultiplierDenominator <= 0 || value.Retry.JitterBasisPoints is < 0 or > 10_000)
            throw new InvalidOperationException("base.activation.definitionInvalid");
    }

    private static byte[] ComputeChecksum(BaseActivationDefinition value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "base.activation.definition.v2\0"); Append(hash, value.Id); Append(hash, value.Version);
        Append(hash, value.OwningModuleId); Append(hash, (int)value.ExecutionClass); Append(hash, value.InputTypeId); Append(hash, value.ResultTypeId);
        Append(hash, value.Grants.Enqueue); Append(hash, value.Grants.Observe); Append(hash, value.Grants.Claim);
        Append(hash, value.Grants.Execute); Append(hash, value.Grants.Renew); Append(hash, value.Grants.Complete);
        Append(hash, value.Grants.Fail); Append(hash, value.Grants.Cancel); Append(hash, value.Grants.Inspect);
        Append(hash, value.Grants.Replay); Append(hash, value.Grants.Migrate); Append(hash, value.Grants.Reconcile); Append(hash, value.Grants.Retry);
        Append(hash, value.Grants.Dispose); Append(hash, value.Grants.Remove); Append(hash, value.Grants.Repair);
        foreach (string grant in value.SourceGrantIds) Append(hash, grant);
        Append(hash, value.Retry.MaximumAttempts); Append(hash, value.Retry.InitialDelayMilliseconds); Append(hash, value.Retry.MaximumDelayMilliseconds);
        Append(hash, value.Retry.MultiplierNumerator); Append(hash, value.Retry.MultiplierDenominator); Append(hash, value.Retry.JitterBasisPoints);
        foreach (string code in value.Retry.RetryableFailureCodes) Append(hash, code);
        Append(hash, value.Limits.MaximumInputBytes); Append(hash, value.Limits.MaximumResultBytes); Append(hash, value.Limits.MaximumAttempts);
        Append(hash, value.Limits.MaximumRenewalsPerAttempt); Append(hash, value.Limits.MaximumChildrenPerAttempt); Append(hash, value.Limits.MaximumLineageDepth);
        Append(hash, value.Limits.LeaseDuration.Ticks); Append(hash, value.Limits.HandlerTimeout.Ticks);
        if (value.Handler is not null) { Append(hash, value.Handler.Id); Append(hash, value.Handler.Version); Append(hash, value.Handler.FactoryId); Append(hash, value.Handler.Checksum.AsSpan()); }
        switch (value.TransactionalTarget)
        {
            case BaseSelectionMutationActivationTarget selection:
                Append(hash, 1); Append(hash, selection.ProfileId); Append(hash, selection.ProfileVersion); Append(hash, selection.ProfileChecksum); break;
            case BaseModuleMutationActivationTarget module:
                Append(hash, 2); Append(hash, module.OperationId); Append(hash, module.OperationVersion); Append(hash, module.OperationChecksum); break;
            default: Append(hash, 0); break;
        }
        return hash.GetHashAndReset();
    }

    private static void ValidateTarget(BaseTransactionalActivationTarget? target)
    {
        switch (target)
        {
            case null: return;
            case BaseSelectionMutationActivationTarget value when value.ProfileVersion > 0 && ValidSha256(value.ProfileChecksum):
                BaseApplicationId.Validate(value.ProfileId, nameof(value.ProfileId)); return;
            case BaseModuleMutationActivationTarget value when value.OperationVersion > 0 && ValidSha256(value.OperationChecksum):
                BaseApplicationId.Validate(value.OperationId, nameof(value.OperationId)); return;
            default: throw new InvalidOperationException("base.activation.definitionInvalid");
        }
    }

    private static bool ValidSha256(string value) => value.Length == 64 && value.All(static character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Append(IncrementalHash hash, string value)
    { byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length)); hash.AppendData(length); hash.AppendData(bytes); }
    private static void Append(IncrementalHash hash, int value) => Append(hash, (long)value);
    private static void Append(IncrementalHash hash, long value)
    { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }
    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)value.Length)); hash.AppendData(length); hash.AppendData(value); }

    private static BaseActivationGrantSet CloneGrants(BaseActivationGrantSet value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value with
        {
            Enqueue = new string(value.Enqueue.AsSpan()), Observe = new string(value.Observe.AsSpan()),
            Claim = new string(value.Claim.AsSpan()), Execute = new string(value.Execute.AsSpan()),
            Renew = new string(value.Renew.AsSpan()), Complete = new string(value.Complete.AsSpan()),
            Fail = new string(value.Fail.AsSpan()), Cancel = new string(value.Cancel.AsSpan()),
            Inspect = new string(value.Inspect.AsSpan()), Replay = new string(value.Replay.AsSpan()),
            Migrate = new string(value.Migrate.AsSpan()), Reconcile = new string(value.Reconcile.AsSpan()),
            Retry = new string(value.Retry.AsSpan()),
            Dispose = new string(value.Dispose.AsSpan()), Remove = new string(value.Remove.AsSpan()),
            Repair = new string(value.Repair.AsSpan()),
        };
    }

    private static void ValidateGrants(BaseActivationGrantSet value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BaseApplicationId.Validate(value.Enqueue, nameof(value.Enqueue)); BaseApplicationId.Validate(value.Observe, nameof(value.Observe));
        BaseApplicationId.Validate(value.Claim, nameof(value.Claim)); BaseApplicationId.Validate(value.Execute, nameof(value.Execute));
        BaseApplicationId.Validate(value.Renew, nameof(value.Renew)); BaseApplicationId.Validate(value.Complete, nameof(value.Complete));
        BaseApplicationId.Validate(value.Fail, nameof(value.Fail)); BaseApplicationId.Validate(value.Cancel, nameof(value.Cancel));
        BaseApplicationId.Validate(value.Inspect, nameof(value.Inspect)); BaseApplicationId.Validate(value.Replay, nameof(value.Replay));
        BaseApplicationId.Validate(value.Migrate, nameof(value.Migrate)); BaseApplicationId.Validate(value.Reconcile, nameof(value.Reconcile));
        BaseApplicationId.Validate(value.Retry, nameof(value.Retry));
        BaseApplicationId.Validate(value.Dispose, nameof(value.Dispose)); BaseApplicationId.Validate(value.Remove, nameof(value.Remove));
        BaseApplicationId.Validate(value.Repair, nameof(value.Repair));
    }
}
