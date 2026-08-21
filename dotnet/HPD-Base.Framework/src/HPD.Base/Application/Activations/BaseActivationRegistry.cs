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
    /// <summary>Gets the exact enqueue grant identity.</summary>
    public required string EnqueueGrantId { get; init; }
    /// <summary>Gets the exact execution grant identity.</summary>
    public required string ExecuteGrantId { get; init; }
    /// <summary>Gets exact declared source-grant identities.</summary>
    public required ImmutableArray<string> SourceGrantIds { get; init; }
    /// <summary>Gets deterministic retry policy.</summary>
    public required BaseActivationRetryProfile Retry { get; init; }
    /// <summary>Gets effective definition limits.</summary>
    public required BaseActivationLimits Limits { get; init; }
    /// <summary>Gets the worker handler binding, forbidden for transactional operations.</summary>
    public BaseActivationHandlerBinding? Handler { get; init; }
    /// <summary>Gets the Runtime-owned canonical checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
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
    internal BaseActivationContext(
        BaseActivationDefinitionKey definition,
        BaseActivationClaimAuthority claim,
        BaseActivationLeaseObservation lease,
        CancellationToken cancellationToken)
    {
        Definition = definition;
        Claim = claim;
        Lease = lease;
        CancellationToken = cancellationToken;
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
}

internal interface IBaseActivationRegistration
{
    BaseActivationDefinition Definition { get; }
    object Identity { get; }
    object CreateHandler(IServiceProvider services);
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
            EnqueueGrantId = new string(source.EnqueueGrantId.AsSpan()),
            ExecuteGrantId = new string(source.ExecuteGrantId.AsSpan()),
            SourceGrantIds = source.SourceGrantIds.Order(StringComparer.Ordinal).Select(static value => new string(value.AsSpan())).ToImmutableArray(),
            Retry = source.Retry with
            {
                RetryableFailureCodes = source.Retry.RetryableFailureCodes.Order(StringComparer.Ordinal)
                    .Select(static value => new string(value.AsSpan())).ToImmutableArray(),
            },
            Limits = source.Limits with { Provider = source.Limits.Provider with { } },
            Handler = source.Handler is null ? null : source.Handler with { Checksum = source.Handler.Checksum.ToArray().ToImmutableArray() },
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
        BaseApplicationId.Validate(value.EnqueueGrantId, nameof(value.EnqueueGrantId));
        BaseApplicationId.Validate(value.ExecuteGrantId, nameof(value.ExecuteGrantId));
        if (value.Version <= 0 || string.IsNullOrWhiteSpace(value.InputTypeId) || string.IsNullOrWhiteSpace(value.ResultTypeId))
            throw new InvalidOperationException("base.activation.definitionInvalid");
        if (value.ExecutionClass == BaseActivationExecutionClass.TransactionalOperation || value.Handler is null)
            throw new InvalidOperationException("base.activation.definitionInvalid");
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
        Append(hash, value.EnqueueGrantId); Append(hash, value.ExecuteGrantId);
        foreach (string grant in value.SourceGrantIds) Append(hash, grant);
        Append(hash, value.Retry.MaximumAttempts); Append(hash, value.Retry.InitialDelayMilliseconds); Append(hash, value.Retry.MaximumDelayMilliseconds);
        Append(hash, value.Retry.MultiplierNumerator); Append(hash, value.Retry.MultiplierDenominator); Append(hash, value.Retry.JitterBasisPoints);
        foreach (string code in value.Retry.RetryableFailureCodes) Append(hash, code);
        Append(hash, value.Limits.MaximumInputBytes); Append(hash, value.Limits.MaximumResultBytes); Append(hash, value.Limits.MaximumAttempts);
        Append(hash, value.Limits.MaximumRenewalsPerAttempt); Append(hash, value.Limits.MaximumChildrenPerAttempt); Append(hash, value.Limits.MaximumLineageDepth);
        Append(hash, value.Limits.LeaseDuration.Ticks); Append(hash, value.Limits.HandlerTimeout.Ticks);
        if (value.Handler is not null) { Append(hash, value.Handler.Id); Append(hash, value.Handler.Version); Append(hash, value.Handler.FactoryId); Append(hash, value.Handler.Checksum.AsSpan()); }
        return hash.GetHashAndReset();
    }

    private static void Append(IncrementalHash hash, string value)
    { byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length)); hash.AppendData(length); hash.AppendData(bytes); }
    private static void Append(IncrementalHash hash, int value) => Append(hash, (long)value);
    private static void Append(IncrementalHash hash, long value)
    { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }
    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> value)
    { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)value.Length)); hash.AppendData(length); hash.AppendData(value); }
}
