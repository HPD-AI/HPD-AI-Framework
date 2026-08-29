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
    /// <summary>Gets the maximum durable yields.</summary>
    public required long MaximumYields { get; init; }
    /// <summary>Gets the maximum renewals per execution slice.</summary>
    public required int MaximumRenewalsPerSlice { get; init; }
    /// <summary>Gets the maximum guarded children per execution slice.</summary>
    public required int MaximumChildrenPerSlice { get; init; }
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
    /// <summary>Gets the stable handler semantic-authority identity.</summary>
    public string SemanticAuthorityId { get; init; } = string.Empty;
    /// <summary>Gets the positive handler semantic-authority version.</summary>
    public int SemanticAuthorityVersion { get; init; }
    /// <summary>Gets the Runtime-owned semantic-authority checksum.</summary>
    public ImmutableArray<byte> SemanticAuthorityChecksum { get; init; }
    /// <summary>Gets the Runtime-owned canonical checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains application-authored handler binding data without Runtime-owned checksums.</summary>
public sealed record BaseActivationHandlerDraft
{
    /// <summary>Gets the stable handler identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive handler version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the stable factory identity.</summary>
    public required string FactoryId { get; init; }
    /// <summary>Gets the exact worker subject kind.</summary>
    public required AccessSubjectKind WorkerSubjectKind { get; init; }
    /// <summary>Gets the reviewed handler semantic authority.</summary>
    public required BaseActivationHandlerSemanticAuthority SemanticAuthority { get; init; }
}

/// <summary>Contains Runtime-checksummed immutable semantic authority for one configured handler.</summary>
public sealed class BaseActivationHandlerSemanticAuthority
{
    private BaseActivationHandlerSemanticAuthority(string id, int version, byte[] artifact, byte[] checksum)
    { Id = id; Version = version; CanonicalArtifact = artifact; Checksum = checksum; }
    /// <summary>Gets the stable semantic-authority identity.</summary>
    public string Id { get; }
    /// <summary>Gets the positive semantic-authority version.</summary>
    public int Version { get; }
    /// <summary>Gets a defensive copy of the canonical semantic artifact.</summary>
    public ReadOnlyMemory<byte> CanonicalArtifact { get; }
    /// <summary>Gets the Runtime-owned semantic-authority checksum.</summary>
    public ReadOnlyMemory<byte> Checksum { get; }

    /// <summary>Creates semantic authority from reviewed canonical bytes without accepting a raw checksum.</summary>
    public static BaseActivationHandlerSemanticAuthority Create(
        string id, int version, ReadOnlySpan<byte> canonicalArtifact = default)
    {
        BaseApplicationId.Validate(id, nameof(id));
        if (version < 1 || canonicalArtifact.Length > 4 * 1024 * 1024)
            throw new InvalidOperationException("base.activation.definitionInvalid");
        byte[] artifact = canonicalArtifact.ToArray();
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendSemantic(hash, "base.activation.handler.semantic.v1\0"); AppendSemantic(hash, id);
        Span<byte> encoded = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(encoded, version);
        hash.AppendData(encoded); AppendSemantic(hash, artifact);
        return new(new string(id.AsSpan()), version, artifact, hash.GetHashAndReset());
    }

    private static void AppendSemantic(IncrementalHash hash, string value) => AppendSemantic(hash, System.Text.Encoding.UTF8.GetBytes(value));
    private static void AppendSemantic(IncrementalHash hash, ReadOnlySpan<byte> value)
    { Span<byte> size = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(size, value.Length); hash.AppendData(size); hash.AppendData(value); }
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
    /// <summary>Gets the exact durable-yield grant identity.</summary>
    public required string Yield { get; init; }
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

/// <summary>Identifies whether physical activation-receipt deletion requires authenticated backup coverage.</summary>
public enum BaseActivationProtectedBackupCoverage
{
    /// <summary>Receipt compaction does not require a protected-backup checkpoint.</summary>
    NotRequired = 0,
    /// <summary>Receipt compaction requires a checkpoint covering the exact receipt-chain prefix.</summary>
    Required = 1,
}

/// <summary>Defines exact duplicate-resolution and protected-backup floors for activation receipts.</summary>
public sealed record BaseActivationReceiptRetentionPolicy
{
    /// <summary>Gets the closed policy format version; L76 requires exactly one.</summary>
    public required int FormatVersion { get; init; }
    /// <summary>Gets the exact whole-millisecond duplicate-resolution lifetime.</summary>
    public required TimeSpan DuplicateResolutionLifetime { get; init; }
    /// <summary>Gets whether authenticated protected-backup coverage is required before deletion.</summary>
    public required BaseActivationProtectedBackupCoverage ProtectedBackupCoverage { get; init; }
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
    /// <summary>Gets the generated input DTO-authority checksum.</summary>
    public ImmutableArray<byte> InputDtoAuthorityChecksum { get; init; }
    /// <summary>Gets the generated result DTO-authority checksum.</summary>
    public ImmutableArray<byte> ResultDtoAuthorityChecksum { get; init; }
    /// <summary>Gets the paired generated DTO-authority checksum.</summary>
    public ImmutableArray<byte> DtoAuthorityChecksum { get; init; }
    /// <summary>Gets the canonical L42 input-field graph checksum.</summary>
    public ImmutableArray<byte> InputDisclosureChecksum { get; init; }
    /// <summary>Gets the canonical L42 result-field graph checksum.</summary>
    public ImmutableArray<byte> ResultDisclosureChecksum { get; init; }
    /// <summary>Gets the complete closed operation-grant authority.</summary>
    public required BaseActivationGrantSet Grants { get; init; }
    /// <summary>Gets exact declared source-grant identities.</summary>
    public required ImmutableArray<string> SourceGrantIds { get; init; }
    /// <summary>Gets deterministic retry policy.</summary>
    public required BaseActivationRetryProfile Retry { get; init; }
    /// <summary>Gets exact activation-receipt retention authority.</summary>
    public required BaseActivationReceiptRetentionPolicy ReceiptRetention { get; init; }
    /// <summary>Gets effective definition limits.</summary>
    public required BaseActivationLimits Limits { get; init; }
    /// <summary>Gets the worker handler binding, forbidden for transactional operations.</summary>
    public BaseActivationHandlerBinding? Handler { get; init; }
    /// <summary>Gets the closed installed target for a transactional activation.</summary>
    public BaseTransactionalActivationTarget? TransactionalTarget { get; init; }
    /// <summary>Gets the Runtime-owned canonical checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains only application-authored activation definition inputs.</summary>
public sealed record BaseActivationDefinitionDraft
{
    /// <summary>Gets the stable activation identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the execution class.</summary>
    public required BaseActivationExecutionClass ExecutionClass { get; init; }
    /// <summary>Gets complete grants.</summary>
    public required BaseActivationGrantSet Grants { get; init; }
    /// <summary>Gets exact source grants.</summary>
    public required ImmutableArray<string> SourceGrantIds { get; init; }
    /// <summary>Gets retry authority.</summary>
    public required BaseActivationRetryProfile Retry { get; init; }
    /// <summary>Gets exact activation-receipt retention authority.</summary>
    public required BaseActivationReceiptRetentionPolicy ReceiptRetention { get; init; }
    /// <summary>Gets exact execution limits.</summary>
    public required BaseActivationLimits Limits { get; init; }
    /// <summary>Gets the worker handler draft.</summary>
    public BaseActivationHandlerDraft? Handler { get; init; }
    /// <summary>Gets the closed transactional target.</summary>
    public BaseTransactionalActivationTarget? TransactionalTarget { get; init; }
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
    private readonly BaseSession _session;
    private readonly int _maximumRenewals;
    private readonly Func<BaseActivationLeaseObservation, CancellationToken, ValueTask<OperationResult<BaseActivationRenewResult>>> _renew;
    private readonly SemaphoreSlim _renewalLock = new(1, 1);
    private readonly Dictionary<(string StepId, int Ordinal), byte[]> _children = [];
    private int _renewals;

    internal BaseActivationContext(
        BaseActivationDefinitionKey definition,
        BaseActivationClaimAuthority claim,
        BaseActivationLeaseObservation lease,
        BaseOwnedSubjectScopeEvidence scope,
        string? occurrenceId,
        long requestedDueAt,
        long effectiveDueAt,
        int maximumRenewals,
        Func<BaseActivationLeaseObservation, CancellationToken, ValueTask<OperationResult<BaseActivationRenewResult>>> renew,
        CancellationToken cancellationToken,
        int maximumChildren,
        BaseSession session)
    {
        Definition = definition;
        Claim = claim;
        Lease = lease;
        Scope = scope with { };
        OccurrenceId = occurrenceId;
        RequestedDueAt = requestedDueAt;
        EffectiveDueAt = effectiveDueAt;
        _maximumRenewals = maximumRenewals;
        _renew = renew;
        CancellationToken = cancellationToken;
        _maximumChildren = maximumChildren;
        _session = session;
    }

    /// <summary>Gets the exact installed definition.</summary>
    public BaseActivationDefinitionKey Definition { get; }
    /// <summary>Gets stable current claim authority.</summary>
    public BaseActivationClaimAuthority Claim { get; }
    /// <summary>Gets the current immutable lease observation.</summary>
    public BaseActivationLeaseObservation Lease { get; internal set; }
    /// <summary>Gets the immutable protected semantic scope inherited from the activation.</summary>
    public BaseOwnedSubjectScopeEvidence Scope { get; }
    /// <summary>Gets the immutable schedule occurrence identity, when scheduled.</summary>
    public string? OccurrenceId { get; }
    /// <summary>Gets the requested due instant as Unix milliseconds.</summary>
    public long RequestedDueAt { get; }
    /// <summary>Gets the effective due instant after deterministic schedule policy.</summary>
    public long EffectiveDueAt { get; }
    /// <summary>Gets the cancellation signal for cooperative handler work.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Opens typed collection operations through this activation's principal-bound session.</summary>
    /// <typeparam name="T">The registered record type.</typeparam>
    /// <param name="collection">The generated collection contract.</param>
    /// <returns>A collection session that cannot escape this activation's principal and application graph.</returns>
    public BaseCollectionSession<T> Collection<T>(BaseCollection<T> collection) => _session.Collection(collection);

    /// <summary>Gets installed lifecycle consumers through this activation's principal-bound session.</summary>
    public BaseSubjectLifecycleSession SubjectLifecycle => _session.SubjectLifecycle;

    /// <summary>Gets installed retirement consumers through this activation's principal-bound session.</summary>
    public BaseSubjectRetirementSession SubjectRetirements => _session.SubjectRetirements;

    /// <summary>Gets installed registered reads through this activation's principal-bound session.</summary>
    public BaseSessionReads Reads => _session.Reads;

    /// <summary>
    /// Creates the exact installed L50 request identity for a child operation without
    /// exposing the activation's principal-bound session or provider authority.
    /// </summary>
    /// <typeparam name="TRequest">The generated request type.</typeparam>
    /// <typeparam name="TResult">The generated result type.</typeparam>
    /// <param name="operation">The installed generated operation identity.</param>
    /// <param name="request">The complete request.</param>
    /// <param name="idempotencyKey">The stable child-attempt key.</param>
    /// <returns>An identity bound to the activation principal, tenant, operation, and request.</returns>
    public BaseMutationRequestIdentity CreateModuleMutationRequestIdentity<TRequest, TResult>(
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> operation,
        TRequest request,
        string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return _session.ModuleMutations.Get(operation).CreateRequestIdentity(request, idempotencyKey);
    }

    /// <summary>Renews the current lease and atomically publishes its replacement observation.</summary>
    public async ValueTask<OperationResult<BaseActivationLeaseObservation>> RenewAsync(
        CancellationToken cancellationToken = default)
    {
        await _renewalLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_renewals >= _maximumRenewals)
                return new OperationResult<BaseActivationLeaseObservation>
                {
                    Status = OperationStatus.ValidationFailed,
                    Error = new BaseError
                    {
                        Code = "base.activation.budgetExceeded",
                        Message = "The activation renewal limit was exceeded.",
                        Category = ErrorCategory.Validation,
                    },
                };
            OperationResult<BaseActivationRenewResult> renewed = await _renew(Lease, cancellationToken).ConfigureAwait(false);
            if (!renewed.IsSuccess() || renewed.Value is null)
                return new OperationResult<BaseActivationLeaseObservation>
                {
                    Status = renewed.Status, Error = renewed.Error,
                    Warnings = renewed.Warnings, Diagnostics = renewed.Diagnostics,
                };
            if (!ReferenceEquals(renewed.Value.Claim, Claim)
                && (renewed.Value.Claim.ActivationId != Claim.ActivationId
                    || renewed.Value.Claim.ClaimEpoch != Claim.ClaimEpoch
                    || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                        renewed.Value.Claim.FencingToken.AsSpan(), Claim.FencingToken.AsSpan())))
            {
                (_session.Services.GetService(typeof(BaseActivationProviderExecutionGate)) as BaseActivationProviderExecutionGate)
                    ?.QuarantineContractViolation();
                return BaseActivationFailureContract.ProviderContractInvalid<BaseActivationLeaseObservation>();
            }
            Lease = renewed.Value.Lease;
            _renewals = checked(_renewals + 1);
            return OperationResults.Ok(Lease);
        }
        finally { _renewalLock.Release(); }
    }

    /// <summary>Derives one deterministic child request identity without minting a fence.</summary>
    public BaseMutationRequestIdentity DeriveChildIdentity(string stepId, int childOrdinal, BaseMutationRequestFingerprint fingerprint)
    {
        BaseApplicationId.Validate(stepId, nameof(stepId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(childOrdinal);
        return BaseMutationRequestIdentity.Create(
            $"activation:{Claim.ActivationId}:slice:{Claim.ExecutionSliceOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
            stepId,
            childOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            fingerprint);
    }

    /// <summary>Creates one same-store fence for a receipt-safe child operation.</summary>
    public BaseActivationGuard GuardChild(
        string stepId,
        int childOrdinal,
        BaseMutationRequestFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        BaseApplicationId.Validate(stepId, nameof(stepId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(childOrdinal);
        lock (_children)
        {
            var key = (stepId, childOrdinal);
            byte[] requested = fingerprint.ToArray();
            if (_children.TryGetValue(key, out byte[]? existing))
            {
                if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(existing, requested))
                    throw new InvalidOperationException("base.activation.childIdentityConflict");
            }
            else if (_children.Count >= _maximumChildren)
                throw new InvalidOperationException("base.activation.childLimitExceeded");
            else _children.Add(key, requested);
        }
        return new BaseActivationGuard
        {
            Claim = Claim,
            StepId = new string(stepId.AsSpan()),
            ChildOrdinal = childOrdinal,
            ChildRequestFingerprint = fingerprint.ToArray().ToImmutableArray(),
        };
    }

    /// <summary>Creates L50 execution options fenced to this exact live claim.</summary>
    public BaseModuleMutationExecutionOptions GuardModuleMutation(
        string stepId,
        int childOrdinal,
        BaseMutationRequestFingerprint fingerprint,
        BaseModuleMutationExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        return (options ?? new BaseModuleMutationExecutionOptions()) with
        {
            ActivationGuard = GuardChild(stepId, childOrdinal, fingerprint),
        };
    }

    /// <summary>Creates one identified atomic L30 batch fenced to this exact live claim.</summary>
    public BaseBatchBuilder GuardRecordMutations(
        string stepId,
        int childOrdinal,
        BaseMutationRequestIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new BaseBatchBuilder(
            _session,
            BaseRecordBatchExecutionMode.Atomic,
            identity,
            GuardChild(stepId, childOrdinal, identity.Fingerprint));
    }

    /// <summary>Creates L43 execution options fenced to this exact live claim.</summary>
    public BaseSelectionMutationExecutionOptions GuardSelectionMutation(
        string stepId,
        int childOrdinal,
        BaseMutationRequestIdentity identity,
        BaseSelectionMutationExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return (options ?? new BaseSelectionMutationExecutionOptions()) with
        {
            ActivationGuard = GuardChild(stepId, childOrdinal, identity.Fingerprint),
        };
    }

    /// <summary>Creates final-retirement execution options fenced to this exact live claim.</summary>
    public BaseSubjectFinalRetirementExecutionOptions GuardSubjectFinalRetirement(
        string stepId,
        int childOrdinal,
        BaseMutationRequestIdentity identity,
        BaseSubjectFinalRetirementExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return (options ?? new BaseSubjectFinalRetirementExecutionOptions()) with
        {
            ActivationGuard = GuardChild(stepId, childOrdinal, identity.Fingerprint),
        };
    }

    /// <summary>Creates final-purge execution options fenced to this exact live claim.</summary>
    public BaseSubjectFinalPurgeExecutionOptions GuardSubjectFinalPurge(
        string stepId,
        int childOrdinal,
        BaseMutationRequestIdentity identity,
        BaseSubjectFinalPurgeExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return (options ?? new BaseSubjectFinalPurgeExecutionOptions()) with
        {
            ActivationGuard = GuardChild(stepId, childOrdinal, identity.Fingerprint),
        };
    }

    /// <summary>Creates the L47 checkpoint fence for one exact identified child.</summary>
    public BaseActivationGuard GuardLifecycleCheckpoint(
        string stepId,
        int childOrdinal,
        BaseMutationRequestIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return GuardChild(stepId, childOrdinal, identity.Fingerprint);
    }

    /// <summary>Creates the L48 acknowledgement fence for one exact identified child.</summary>
    public BaseActivationGuard GuardRetirementAcknowledgement(
        string stepId,
        int childOrdinal,
        BaseMutationRequestIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return GuardChild(stepId, childOrdinal, identity.Fingerprint);
    }

    /// <summary>
    /// Executes an installed module mutation through this activation's
    /// principal-bound session without exposing provider transaction authority.
    /// </summary>
    public ValueTask<BaseResult<BaseModuleMutationExecutionResult<TResult>>> ExecuteModuleMutationAsync<TRequest, TResult>(
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> operation,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseModuleMutationExecutionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(identity);
        return _session.ModuleMutations.Get(operation)
            .ExecuteAsync(request, identity, options, cancellationToken);
    }

    /// <summary>
    /// Creates L50 options that atomically persist guarded module state and one
    /// graph-installed child activation in the same provider transaction.
    /// </summary>
    public BaseModuleMutationExecutionOptions GuardModuleMutationAndCreateActivation<TInput, TResult>(
        string stepId,
        int childOrdinal,
        BaseMutationRequestFingerprint fingerprint,
        BaseActivationRegistrationIdentity<TInput, TResult> activation,
        TInput input,
        long requestedDueAt,
        string activationStepId,
        int activationOrdinal,
        BaseModuleMutationExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentOutOfRangeException.ThrowIfNegative(requestedDueAt);
        BaseApplicationId.Validate(activationStepId, nameof(activationStepId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(activationOrdinal);
        BaseActivationGuard guard = GuardChild(stepId, childOrdinal, fingerprint);
        byte[] canonicalInput = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(input, activation.Input);
        byte[] inputChecksum = System.Security.Cryptography.SHA256.HashData(canonicalInput);
        BaseMutationRequestIdentity childIdentity = DeriveChildIdentity(
            activationStepId, activationOrdinal, fingerprint);
        var intent = new BaseActivationCreateIntent
        {
            Ordinal = 0,
            Definition = new BaseActivationDefinitionKey
            {
                Id = activation.Id,
                Version = activation.Version,
                Checksum = activation.Checksum.ToArray().ToImmutableArray(),
            },
            MaximumYields = activation.MaximumYields,
            ReceiptRetention = activation.ReceiptRetention with { },
            CanonicalInput = canonicalInput.ToImmutableArray(),
            InputChecksum = inputChecksum.ToImmutableArray(),
            Scope = Scope with { },
            RequestedDueAt = requestedDueAt,
            EffectiveDueAt = requestedDueAt,
            Priority = 0,
            OverlapKey = [],
            OverlapPolicy = BaseScheduleOverlapPolicy.Allow,
            InitiallyEligible = true,
            Identity = childIdentity,
        };
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.childCreation.v1\0"u8);
        hash.AppendData(guard.ChildRequestFingerprint.AsSpan());
        hash.AppendData(Encoding.UTF8.GetBytes(activation.Id));
        hash.AppendData(activation.Checksum.Span);
        hash.AppendData(inputChecksum);
        Span<byte> due = stackalloc byte[sizeof(long)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(due, requestedDueAt);
        hash.AppendData(due);
        return (options ?? new BaseModuleMutationExecutionOptions()) with
        {
            ActivationGuard = guard,
            ActivationCreation = new BaseActivationCreationExtension
            {
                Items = [intent],
                StructuralDigest = hash.GetHashAndReset().ToImmutableArray(),
            },
        };
    }

    /// <summary>Creates L50 options that ensure one parent-independent semantic activation atomically.</summary>
    public BaseSemanticActivationKey<TDefinition> CreateSemanticActivationKey<TRequest, TDefinition>(
        BaseSemanticActivationKeyIdentity<TRequest, TDefinition> identity,
        TRequest request)
    {
        ArgumentNullException.ThrowIfNull(identity);
        BaseSemanticActivationRegistry registry = _session.Services.GetService(typeof(BaseSemanticActivationRegistry)) as BaseSemanticActivationRegistry
            ?? throw new InvalidOperationException("base.semanticActivation.notInstalled");
        return registry.CreateKey(identity, request);
    }

    /// <summary>Creates L50 options that ensure one parent-independent semantic activation atomically.</summary>
    public BaseModuleMutationExecutionOptions GuardModuleMutationAndEnsureActivation<TInput, TResult, TDefinition>(
        string stepId,
        int childOrdinal,
        BaseMutationRequestFingerprint fingerprint,
        BaseActivationRegistrationIdentity<TInput, TResult> activation,
        TInput input,
        DateTimeOffset? dueAt,
        BaseSemanticActivationKey<TDefinition> semanticKey,
        BaseModuleMutationExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(semanticKey);
        if (dueAt is { Offset: not { Ticks: 0 } })
            throw new InvalidOperationException("base.semanticActivation.dueInvalid");
        BaseActivationGuard guard = GuardChild(stepId, childOrdinal, fingerprint);
        byte[] canonicalInput = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(input, activation.Input);
        return (options ?? new BaseModuleMutationExecutionOptions()) with
        {
            ActivationGuard = guard,
            SemanticActivation = new BaseSemanticActivationGuardedEnsureRequest
            {
                Key = semanticKey,
                Scope = Scope with { Value = Scope.Value is null ? null : new string(Scope.Value.AsSpan()) },
                Activation = new BaseActivationDefinitionKey
                {
                    Id = new string(activation.Id.AsSpan()),
                    Version = activation.Version,
                    Checksum = activation.Checksum.ToArray().ToImmutableArray(),
                },
                CanonicalInput = canonicalInput.ToImmutableArray(),
                InputChecksum = System.Security.Cryptography.SHA256.HashData(canonicalInput).ToImmutableArray(),
                DueAt = dueAt,
            },
        };
    }

    /// <summary>Creates L50 options that retire one terminal parent-independent semantic activation.</summary>
    public BaseModuleMutationExecutionOptions GuardModuleMutationAndRetireSemanticActivation<TDefinition>(
        string stepId,
        int childOrdinal,
        BaseMutationRequestFingerprint fingerprint,
        BaseSemanticActivationKey<TDefinition> semanticKey,
        BaseModuleMutationExecutionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(semanticKey);
        return (options ?? new BaseModuleMutationExecutionOptions()) with
        {
            ActivationGuard = GuardChild(stepId, childOrdinal, fingerprint),
            SemanticActivation = new BaseSemanticActivationGuardedRetireRequest
            {
                Key = semanticKey,
                Scope = Scope with { Value = Scope.Value is null ? null : new string(Scope.Value.AsSpan()) },
            },
        };
    }
}

/// <summary>Base of the closed activation-handler result union.</summary>
public abstract record BaseActivationHandlerResult<TResult>
{
    private protected BaseActivationHandlerResult() { }
}

/// <summary>Reports successful logical completion.</summary>
public sealed record BaseActivationSucceeded<TResult> : BaseActivationHandlerResult<TResult>
{
    /// <summary>Gets the successful result.</summary>
    public required TResult Result { get; init; }
}

/// <summary>Reports one stable failed handler outcome.</summary>
public sealed record BaseActivationFailed<TResult> : BaseActivationHandlerResult<TResult>
{
    /// <summary>Gets the stable safe failure code.</summary>
    public required string FailureCode { get; init; }
    /// <summary>Gets whether the failure may enter deterministic retry.</summary>
    public required bool Retryable { get; init; }
}

/// <summary>Reports committed bounded progress that must resume as the same activation.</summary>
public sealed record BaseActivationYielded<TResult> : BaseActivationHandlerResult<TResult>
{
    /// <summary>Gets the closed durable-yield request.</summary>
    public required BaseActivationYield Yield { get; init; }
}

/// <summary>Contains an inert source-generated activation registration identity.</summary>
public sealed class BaseActivationRegistrationIdentity<TInput, TResult> : IBaseSerializerMetadataSource
{
    private readonly BaseSerializerContextRegistration? _registration;
    private readonly IReadOnlyList<BaseSerializerPropertyDeclaration>? _declarations;
    private readonly BaseGeneratedActivationDtoAuthority<TInput, TResult>? _authority;
    private readonly JsonTypeInfo<TInput>? _legacyInput;
    private readonly JsonTypeInfo<TResult>? _legacyResult;
    /// <summary>Initializes an inert registration identity.</summary>
    internal BaseActivationRegistrationIdentity(
        string id,
        int version,
        ReadOnlyMemory<byte> checksum,
        BaseActivationReceiptRetentionPolicy receiptRetention,
        JsonTypeInfo<TInput> input,
        JsonTypeInfo<TResult> result,
        IReadOnlyList<BaseModuleDtoPropertyBinding> inputBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings)
    {
        Id = new string(id.AsSpan());
        Version = version;
        MaximumYields = 0;
        ReceiptRetention = receiptRetention with { };
        Checksum = checksum.ToArray();
        _legacyInput = input;
        _legacyResult = result;
        InputBindings = FreezeBindings(inputBindings, typeof(TInput));
        ResultBindings = FreezeBindings(resultBindings, typeof(TResult));
    }

    private BaseActivationRegistrationIdentity(
        BaseActivationDefinition definition,
        BaseGeneratedActivationDtoAuthority<TInput, TResult> authority)
    {
        _authority = authority;
        Id = definition.Id; Version = definition.Version; MaximumYields = definition.Limits.MaximumYields;
        ReceiptRetention = definition.ReceiptRetention with { }; Checksum = definition.Checksum.ToArray();
        InputBindings = authority.InputBindings.Values.ToArray(); ResultBindings = authority.ResultBindings.Values.ToArray();
        _registration = authority.SerializerRegistration; _declarations = authority.SerializerDeclarations;
    }

    internal static BaseActivationRegistrationIdentity<TInput, TResult> Generated(
        BaseActivationDefinition definition,
        BaseGeneratedActivationDtoAuthority<TInput, TResult> authority) => new(definition, authority);

    /// <summary>Gets the definition identity.</summary>
    public string Id { get; }
    /// <summary>Gets the definition version.</summary>
    public int Version { get; }
    /// <summary>Gets the immutable maximum durable yields.</summary>
    public long MaximumYields { get; }
    /// <summary>Gets immutable activation-receipt retention authority.</summary>
    public BaseActivationReceiptRetentionPolicy ReceiptRetention { get; }
    /// <summary>Gets the canonical definition checksum.</summary>
    public ReadOnlyMemory<byte> Checksum { get; }
    /// <summary>Gets source-generated input metadata.</summary>
    public JsonTypeInfo<TInput> Input => _authority?.InputTypeInfo ?? _legacyInput
        ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    /// <summary>Gets source-generated result metadata.</summary>
    public JsonTypeInfo<TResult> Result => _authority?.ResultTypeInfo ?? _legacyResult
        ?? throw new InvalidOperationException("base.schema.serializer.ownerRequired");
    /// <summary>Gets the exact graph-owned L42 input-property bindings.</summary>
    public IReadOnlyList<BaseModuleDtoPropertyBinding> InputBindings { get; }
    /// <summary>Gets the exact graph-owned L42 result-property bindings.</summary>
    public IReadOnlyList<BaseModuleDtoPropertyBinding> ResultBindings { get; }
    internal byte[] CanonicalInput(TInput value) => _authority is null
        ? System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, Input)
        : _authority.CanonicalInput(value);
    internal byte[] CanonicalResult(TResult value) => _authority is null
        ? System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, Result)
        : _authority.CanonicalResult(value);
    internal TInput DecodeInput(ReadOnlySpan<byte> value, bool providerInfluenced)
    {
        if (_authority is not null) return _authority.DecodeInput(value, providerInfluenced);
        TInput? decoded = System.Text.Json.JsonSerializer.Deserialize(value, Input);
        return decoded ?? throw new System.Text.Json.JsonException();
    }
    internal TResult DecodeResult(ReadOnlySpan<byte> value, bool providerInfluenced)
    {
        if (_authority is not null) return _authority.DecodeResult(value, providerInfluenced);
        TResult? decoded = System.Text.Json.JsonSerializer.Deserialize(value, Result);
        return decoded ?? throw new System.Text.Json.JsonException();
    }
    internal bool UsesAuthority(BaseGeneratedActivationDtoAuthority<TInput, TResult> authority) =>
        ReferenceEquals(_authority, authority)
        && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            _authority.DtoAuthorityChecksum.Span, authority.DtoAuthorityChecksum.Span);
    IReadOnlyList<System.Text.Json.Serialization.Metadata.JsonTypeInfo> IBaseSerializerMetadataSource.Roots => [Input, Result];
    bool IBaseSerializerMetadataSource.Generated => _registration is not null;
    BaseSerializerContextRegistration? IBaseSerializerMetadataSource.Registration => _registration;
    IReadOnlyList<Type> IBaseSerializerMetadataSource.RootTypes => [typeof(TInput), typeof(TResult)];
    IReadOnlyList<BaseSerializerPropertyDeclaration>? IBaseSerializerMetadataSource.SerializerDeclarations => _declarations;
    CollectionDefinition? IBaseSerializerMetadataSource.CollectionDefinition => null;
    void IBaseSerializerMetadataSource.Bind(BaseSerializerMetadataOwner owner)
    {
        if (_authority is not null) _authority.BindOwner(owner);
    }

    private static IReadOnlyList<BaseModuleDtoPropertyBinding> FreezeBindings(
        IReadOnlyList<BaseModuleDtoPropertyBinding> bindings,
        Type root)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        BaseModuleDtoPropertyBinding[] values = bindings.ToArray();
        if (values.Any(binding => binding.DeclaringType != root)
            || values.Select(static binding => binding.StablePropertyId).Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidOperationException("base.activation.definitionInvalid");
        return Array.AsReadOnly(values);
    }
}

/// <summary>Registers one graph-owned activation handler and its closed codecs.</summary>
public sealed class BaseActivationHandlerRegistration<TInput, TResult>
{
    internal BaseActivationHandlerRegistration(
        BaseActivationDefinition definition,
        BaseActivationRegistrationIdentity<TInput, TResult> identity,
        Func<IServiceProvider, IBaseActivationHandler<TInput, TResult>> factory)
    {
        Definition = definition;
        Identity = identity;
        Factory = factory;
    }

    /// <summary>Gets the sealed activation definition.</summary>
    public BaseActivationDefinition Definition { get; }
    /// <summary>Gets the inert generated identity.</summary>
    public BaseActivationRegistrationIdentity<TInput, TResult> Identity { get; }
    /// <summary>Gets the graph-owned Native-AOT-safe handler factory.</summary>
    internal Func<IServiceProvider, IBaseActivationHandler<TInput, TResult>> Factory { get; }

    internal BaseActivationHandlerRegistration<TInput, TResult> WithDefinition(BaseActivationDefinition definition) =>
        new(definition, Identity, Factory);
}

/// <summary>Registers one handler-free transactional activation and its closed codecs.</summary>
public sealed class BaseTransactionalActivationRegistration<TInput, TResult>
{
    internal BaseTransactionalActivationRegistration(
        BaseActivationDefinition definition,
        BaseActivationRegistrationIdentity<TInput, TResult> identity)
    {
        Definition = definition;
        Identity = identity;
    }

    /// <summary>Gets the sealed activation definition.</summary>
    public BaseActivationDefinition Definition { get; }
    /// <summary>Gets the inert generated identity.</summary>
    public BaseActivationRegistrationIdentity<TInput, TResult> Identity { get; }

    internal BaseTransactionalActivationRegistration<TInput, TResult> WithDefinition(BaseActivationDefinition definition) =>
        new(definition, Identity);
}

/// <summary>Builds one sealed activation registration from closed graph-owned inputs.</summary>
public static class BaseActivationDefinitionBuilder
{
    /// <summary>Creates one sealed worker activation from generated DTO authority.</summary>
    public static BaseActivationHandlerRegistration<TInput, TResult> CreateGenerated<TInput, TResult>(
        BaseActivationDefinitionDraft draft,
        BaseGeneratedActivationDtoAuthority<TInput, TResult> authority,
        Func<IServiceProvider, IBaseActivationHandler<TInput, TResult>> factory)
    {
        ArgumentNullException.ThrowIfNull(draft); ArgumentNullException.ThrowIfNull(authority); ArgumentNullException.ThrowIfNull(factory);
        if (draft.OwningModuleId != authority.OwningModuleId
            || draft.ExecutionClass == BaseActivationExecutionClass.TransactionalOperation || draft.Handler is null
            || draft.TransactionalTarget is not null)
            throw new InvalidOperationException("base.activation.definitionInvalid");
        BaseActivationHandlerDraft handler = draft.Handler;
        ImmutableArray<byte> handlerChecksum = HandlerChecksum(draft, authority, handler).ToImmutableArray();
        BaseActivationDefinition sealedDefinition = BaseActivationContract.Seal(new BaseActivationDefinition
        {
            Id = draft.Id, Version = draft.Version, OwningModuleId = draft.OwningModuleId,
            ExecutionClass = draft.ExecutionClass, InputTypeId = authority.InputTypeId, ResultTypeId = authority.ResultTypeId,
            InputDtoAuthorityChecksum = authority.InputDtoAuthorityChecksum.ToArray().ToImmutableArray(),
            ResultDtoAuthorityChecksum = authority.ResultDtoAuthorityChecksum.ToArray().ToImmutableArray(),
            DtoAuthorityChecksum = authority.DtoAuthorityChecksum.ToArray().ToImmutableArray(),
            InputDisclosureChecksum = authority.InputDisclosureChecksum.ToArray().ToImmutableArray(),
            ResultDisclosureChecksum = authority.ResultDisclosureChecksum.ToArray().ToImmutableArray(),
            Grants = draft.Grants, SourceGrantIds = draft.SourceGrantIds, Retry = draft.Retry,
            ReceiptRetention = draft.ReceiptRetention, Limits = draft.Limits,
            Handler = new BaseActivationHandlerBinding
            {
                Id = handler.Id, Version = handler.Version, FactoryId = handler.FactoryId,
                InputTypeId = authority.InputTypeId, ResultTypeId = authority.ResultTypeId,
                WorkerSubjectKind = handler.WorkerSubjectKind,
                SemanticAuthorityId = handler.SemanticAuthority.Id,
                SemanticAuthorityVersion = handler.SemanticAuthority.Version,
                SemanticAuthorityChecksum = handler.SemanticAuthority.Checksum.ToArray().ToImmutableArray(),
                Checksum = handlerChecksum,
            }, TransactionalTarget = null, Checksum = [],
        });
        return new BaseActivationHandlerRegistration<TInput, TResult>(
            sealedDefinition,
            BaseActivationRegistrationIdentity<TInput, TResult>.Generated(sealedDefinition, authority),
            factory);
    }

    /// <summary>Creates one sealed transactional activation from generated DTO authority.</summary>
    public static BaseTransactionalActivationRegistration<TInput, TResult> CreateGeneratedTransactional<TInput, TResult>(
        BaseActivationDefinitionDraft draft,
        BaseGeneratedActivationDtoAuthority<TInput, TResult> authority)
    {
        ArgumentNullException.ThrowIfNull(draft); ArgumentNullException.ThrowIfNull(authority);
        if (draft.OwningModuleId != authority.OwningModuleId
            || draft.ExecutionClass != BaseActivationExecutionClass.TransactionalOperation || draft.Handler is not null
            || draft.TransactionalTarget is null)
            throw new InvalidOperationException("base.activation.definitionInvalid");
        BaseActivationDefinition sealedDefinition = BaseActivationContract.Seal(new BaseActivationDefinition
        {
            Id = draft.Id, Version = draft.Version, OwningModuleId = draft.OwningModuleId,
            ExecutionClass = draft.ExecutionClass, InputTypeId = authority.InputTypeId, ResultTypeId = authority.ResultTypeId,
            InputDtoAuthorityChecksum = authority.InputDtoAuthorityChecksum.ToArray().ToImmutableArray(),
            ResultDtoAuthorityChecksum = authority.ResultDtoAuthorityChecksum.ToArray().ToImmutableArray(),
            DtoAuthorityChecksum = authority.DtoAuthorityChecksum.ToArray().ToImmutableArray(),
            InputDisclosureChecksum = authority.InputDisclosureChecksum.ToArray().ToImmutableArray(),
            ResultDisclosureChecksum = authority.ResultDisclosureChecksum.ToArray().ToImmutableArray(),
            Grants = draft.Grants, SourceGrantIds = draft.SourceGrantIds, Retry = draft.Retry,
            ReceiptRetention = draft.ReceiptRetention, Limits = draft.Limits,
            Handler = null, TransactionalTarget = draft.TransactionalTarget, Checksum = [],
        });
        return new BaseTransactionalActivationRegistration<TInput, TResult>(
            sealedDefinition,
            BaseActivationRegistrationIdentity<TInput, TResult>.Generated(sealedDefinition, authority));
    }

    private static byte[] HandlerChecksum<TInput, TResult>(BaseActivationDefinitionDraft definition,
        BaseGeneratedActivationDtoAuthority<TInput, TResult> authority, BaseActivationHandlerDraft handler)
    {
        BaseApplicationId.Validate(handler.Id, nameof(handler)); BaseApplicationId.Validate(handler.FactoryId, nameof(handler));
        if (handler.Version < 1 || !Enum.IsDefined(handler.WorkerSubjectKind)) throw new InvalidOperationException("base.activation.definitionInvalid");
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHandler(hash, "base.activation.handler.v2\0"); AppendHandler(hash, definition.Id); AppendHandler(hash, definition.Version);
        AppendHandler(hash, definition.OwningModuleId); AppendHandler(hash, handler.Id); AppendHandler(hash, handler.Version);
        AppendHandler(hash, handler.FactoryId); AppendHandler(hash, (int)handler.WorkerSubjectKind);
        AppendHandler(hash, handler.SemanticAuthority.Id); AppendHandler(hash, handler.SemanticAuthority.Version);
        AppendHandler(hash, handler.SemanticAuthority.Checksum.Span); AppendHandler(hash, authority.DtoAuthorityChecksum.Span);
        AppendHandler(hash, definition.ReceiptRetention.FormatVersion);
        AppendHandler(hash, definition.ReceiptRetention.DuplicateResolutionLifetime.Ticks);
        AppendHandler(hash, (int)definition.ReceiptRetention.ProtectedBackupCoverage);
        return hash.GetHashAndReset();
    }

    private static void AppendHandler(IncrementalHash hash, string value) => AppendHandler(hash, Encoding.UTF8.GetBytes(value));
    private static void AppendHandler(IncrementalHash hash, int value)
    { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); hash.AppendData(bytes); }
    private static void AppendHandler(IncrementalHash hash, long value)
    { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }
    private static void AppendHandler(IncrementalHash hash, ReadOnlySpan<byte> value)
    { Span<byte> size = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(size, value.Length); hash.AppendData(size); hash.AppendData(value); }

    /// <summary>Computes canonical authority and returns one inert registration.</summary>
    internal static BaseActivationHandlerRegistration<TInput, TResult> Create<TInput, TResult>(
        BaseActivationDefinition definition,
        JsonTypeInfo<TInput> input,
        JsonTypeInfo<TResult> result,
        IReadOnlyList<BaseModuleDtoPropertyBinding> inputBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings,
        Func<IServiceProvider, IBaseActivationHandler<TInput, TResult>> factory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(inputBindings);
        ArgumentNullException.ThrowIfNull(resultBindings);
        ArgumentNullException.ThrowIfNull(factory);
        BaseActivationDefinition sealedDefinition = BaseActivationContract.Seal(definition with
        {
            InputDisclosureChecksum = DisclosureChecksum(inputBindings),
            ResultDisclosureChecksum = DisclosureChecksum(resultBindings),
        });
        return new BaseActivationHandlerRegistration<TInput, TResult>(
            sealedDefinition,
            new BaseActivationRegistrationIdentity<TInput, TResult>(
                sealedDefinition.Id, sealedDefinition.Version, sealedDefinition.Checksum.ToArray(),
                sealedDefinition.ReceiptRetention, input, result,
                inputBindings, resultBindings),
            factory);
    }

    /// <summary>Computes canonical authority for one handler-free transactional activation.</summary>
    internal static BaseTransactionalActivationRegistration<TInput, TResult> CreateTransactional<TInput, TResult>(
        BaseActivationDefinition definition,
        JsonTypeInfo<TInput> input,
        JsonTypeInfo<TResult> result,
        IReadOnlyList<BaseModuleDtoPropertyBinding> inputBindings,
        IReadOnlyList<BaseModuleDtoPropertyBinding> resultBindings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(inputBindings);
        ArgumentNullException.ThrowIfNull(resultBindings);
        BaseActivationDefinition sealedDefinition = BaseActivationContract.Seal(definition with
        {
            InputDisclosureChecksum = DisclosureChecksum(inputBindings),
            ResultDisclosureChecksum = DisclosureChecksum(resultBindings),
        });
        return new BaseTransactionalActivationRegistration<TInput, TResult>(
            sealedDefinition,
            new BaseActivationRegistrationIdentity<TInput, TResult>(
                sealedDefinition.Id, sealedDefinition.Version, sealedDefinition.Checksum.ToArray(),
                sealedDefinition.ReceiptRetention, input, result,
                inputBindings, resultBindings));
    }

    private static ImmutableArray<byte> DisclosureChecksum(IReadOnlyList<BaseModuleDtoPropertyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.disclosure.v1\0"u8);
        foreach (BaseModuleDtoPropertyBinding binding in bindings.OrderBy(static value => value.PathKey, StringComparer.Ordinal))
        {
            Append(hash, binding.PathKey);
            Append(hash, binding.ApplicationName);
            Append(hash, binding.DeclaringType.AssemblyQualifiedName ?? binding.DeclaringType.FullName ?? binding.DeclaringType.Name);
            Append(hash, binding.PropertyType?.AssemblyQualifiedName ?? string.Empty);
            Append(hash, (int)binding.Confidentiality);
            Append(hash, (int)binding.RecordDisclosure);
            Append(hash, binding.Nullability == BaseFieldNullability.Nullable ? 1 : 0);
        }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length); hash.AppendData(bytes);
    }

    private static void Append(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}

internal interface IBaseActivationRegistration
{
    BaseActivationDefinition Definition { get; }
    object Identity { get; }
    Type InputType { get; }
    Type ResultType { get; }
    IReadOnlyList<BaseModuleDtoPropertyBinding> ResultBindings { get; }
    object? CreateHandler(IServiceProvider services);
    ValueTask<OperationResult<BaseActivationEnqueueResult>> EnqueueAsync(
        IBaseActivationRuntime runtime,
        BaseSession session,
        ReadOnlyMemory<byte> canonicalInput,
        BaseMutationRequestIdentity identity,
        BaseActivationEnqueueOptions? options,
        CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteAsync(
        IBaseActivationWorkerRuntime runtime,
        BaseSession session,
        BaseActivationClaimAuthority claim,
        ReadOnlyMemory<byte> canonicalResult,
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken);
    ValueTask<OperationResult<BaseActivationDispatchResult>> RunOneAsync(
        IBaseActivationWorkerRuntime runtime,
        BaseSession session,
        CancellationToken cancellationToken);
}

internal sealed class BaseInstalledTransactionalActivationRegistration<TInput, TResult>(
    BaseTransactionalActivationRegistration<TInput, TResult> registration) : IBaseActivationRegistration
{
    public BaseActivationDefinition Definition { get; } = BaseActivationContract.Seal(registration.Definition);
    public object Identity { get; } = registration.Identity;
    public Type InputType => typeof(TInput);
    public Type ResultType => typeof(TResult);
    public IReadOnlyList<BaseModuleDtoPropertyBinding> ResultBindings => registration.Identity.ResultBindings;
    public object? CreateHandler(IServiceProvider services) => null;
    public ValueTask<OperationResult<BaseActivationEnqueueResult>> EnqueueAsync(
        IBaseActivationRuntime runtime, BaseSession session, ReadOnlyMemory<byte> canonicalInput,
        BaseMutationRequestIdentity identity, BaseActivationEnqueueOptions? options,
        CancellationToken cancellationToken) => EnqueueCore(runtime, session, canonicalInput, identity, options, cancellationToken);
    public ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteAsync(
        IBaseActivationWorkerRuntime runtime, BaseSession session, BaseActivationClaimAuthority claim,
        ReadOnlyMemory<byte> canonicalResult, BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken) => CompleteCore(runtime, session, claim, canonicalResult, identity, cancellationToken);
    public ValueTask<OperationResult<BaseActivationDispatchResult>> RunOneAsync(
        IBaseActivationWorkerRuntime runtime, BaseSession session, CancellationToken cancellationToken) =>
        new BaseInstalledActivationWorkerHandle<TInput, TResult>(runtime, session, Definition, registration.Identity)
            .RunOneAsync(cancellationToken);

    private ValueTask<OperationResult<BaseActivationEnqueueResult>> EnqueueCore(
        IBaseActivationRuntime runtime, BaseSession session, ReadOnlyMemory<byte> canonicalInput,
        BaseMutationRequestIdentity identity, BaseActivationEnqueueOptions? options,
        CancellationToken cancellationToken)
    {
        TInput input = registration.Identity.DecodeInput(canonicalInput.Span, providerInfluenced: true);
        return runtime.EnqueueAsync(session, Definition, registration.Identity, input, identity, options, cancellationToken);
    }

    private ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteCore(
        IBaseActivationWorkerRuntime runtime, BaseSession session, BaseActivationClaimAuthority claim,
        ReadOnlyMemory<byte> canonicalResult, BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        TResult result = registration.Identity.DecodeResult(canonicalResult.Span, providerInfluenced: true);
        byte[] bytes = registration.Identity.CanonicalResult(result);
        return runtime.CompleteAsync(session, Definition, claim, bytes.ToImmutableArray(), identity, cancellationToken);
    }
}

internal sealed class BaseActivationRegistration<TInput, TResult>(
    BaseActivationHandlerRegistration<TInput, TResult> registration) : IBaseActivationRegistration
{
    public BaseActivationDefinition Definition { get; } = BaseActivationContract.Seal(registration.Definition);
    public object Identity { get; } = registration.Identity;
    public Type InputType => typeof(TInput);
    public Type ResultType => typeof(TResult);
    public IReadOnlyList<BaseModuleDtoPropertyBinding> ResultBindings => registration.Identity.ResultBindings;
    public object CreateHandler(IServiceProvider services) => registration.Factory(services);
    public ValueTask<OperationResult<BaseActivationEnqueueResult>> EnqueueAsync(
        IBaseActivationRuntime runtime, BaseSession session, ReadOnlyMemory<byte> canonicalInput,
        BaseMutationRequestIdentity identity, BaseActivationEnqueueOptions? options,
        CancellationToken cancellationToken)
    {
        TInput input = registration.Identity.DecodeInput(canonicalInput.Span, providerInfluenced: true);
        return runtime.EnqueueAsync(session, Definition, registration.Identity, input, identity, options, cancellationToken);
    }
    public ValueTask<OperationResult<BaseActivationTransitionResult>> CompleteAsync(
        IBaseActivationWorkerRuntime runtime, BaseSession session, BaseActivationClaimAuthority claim,
        ReadOnlyMemory<byte> canonicalResult, BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        TResult result = registration.Identity.DecodeResult(canonicalResult.Span, providerInfluenced: true);
        byte[] bytes = registration.Identity.CanonicalResult(result);
        return runtime.CompleteAsync(session, Definition, claim, bytes.ToImmutableArray(), identity, cancellationToken);
    }
    public ValueTask<OperationResult<BaseActivationDispatchResult>> RunOneAsync(
        IBaseActivationWorkerRuntime runtime, BaseSession session, CancellationToken cancellationToken) =>
        new BaseInstalledActivationWorkerHandle<TInput, TResult>(runtime, session, Definition, registration.Identity)
            .RunOneAsync(cancellationToken);
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

    internal IReadOnlyList<IBaseActivationRegistration> Registrations => _registrations.Values
        .OrderBy(static registration => registration.Definition.Id, StringComparer.Ordinal)
        .ThenBy(static registration => registration.Definition.Version)
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
            InputDtoAuthorityChecksum = source.InputDtoAuthorityChecksum.IsDefault ? [] : source.InputDtoAuthorityChecksum.ToArray().ToImmutableArray(),
            ResultDtoAuthorityChecksum = source.ResultDtoAuthorityChecksum.IsDefault ? [] : source.ResultDtoAuthorityChecksum.ToArray().ToImmutableArray(),
            DtoAuthorityChecksum = source.DtoAuthorityChecksum.IsDefault ? [] : source.DtoAuthorityChecksum.ToArray().ToImmutableArray(),
            InputDisclosureChecksum = source.InputDisclosureChecksum.ToArray().ToImmutableArray(),
            ResultDisclosureChecksum = source.ResultDisclosureChecksum.ToArray().ToImmutableArray(),
            Grants = CloneGrants(source.Grants),
            SourceGrantIds = source.SourceGrantIds.Order(StringComparer.Ordinal).Select(static value => new string(value.AsSpan())).ToImmutableArray(),
            Retry = source.Retry with
            {
                RetryableFailureCodes = source.Retry.RetryableFailureCodes.Order(StringComparer.Ordinal)
                    .Select(static value => new string(value.AsSpan())).ToImmutableArray(),
            },
            ReceiptRetention = source.ReceiptRetention with { },
            Limits = source.Limits with { Provider = source.Limits.Provider with { }, AtomicCreation = source.Limits.AtomicCreation with { Deadlines = source.Limits.AtomicCreation.Deadlines with { } } },
            Handler = source.Handler is null ? null : source.Handler with
            {
                SemanticAuthorityId = new string(source.Handler.SemanticAuthorityId.AsSpan()),
                SemanticAuthorityChecksum = source.Handler.SemanticAuthorityChecksum.IsDefault ? [] : source.Handler.SemanticAuthorityChecksum.ToArray().ToImmutableArray(),
                Checksum = source.Handler.Checksum.ToArray().ToImmutableArray(),
            },
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
        bool generated = !value.DtoAuthorityChecksum.IsDefaultOrEmpty;
        if (value.Version <= 0 || string.IsNullOrWhiteSpace(value.InputTypeId) || string.IsNullOrWhiteSpace(value.ResultTypeId)
            || value.InputDisclosureChecksum.Length != SHA256.HashSizeInBytes
            || value.ResultDisclosureChecksum.Length != SHA256.HashSizeInBytes
            || generated && (value.InputDtoAuthorityChecksum.Length != SHA256.HashSizeInBytes
                || value.ResultDtoAuthorityChecksum.Length != SHA256.HashSizeInBytes
                || value.DtoAuthorityChecksum.Length != SHA256.HashSizeInBytes)
            || !generated && (!value.InputDtoAuthorityChecksum.IsDefaultOrEmpty || !value.ResultDtoAuthorityChecksum.IsDefaultOrEmpty))
            throw new InvalidOperationException("base.activation.definitionInvalid");
        if (value.ExecutionClass == BaseActivationExecutionClass.TransactionalOperation
            ? value.TransactionalTarget is null || value.Handler is not null
            : value.TransactionalTarget is not null || value.Handler is null)
            throw new InvalidOperationException("base.activation.definitionInvalid");
        if (generated && value.Handler is { } generatedHandler)
        {
            BaseApplicationId.Validate(generatedHandler.SemanticAuthorityId, nameof(value.Handler));
            if (generatedHandler.SemanticAuthorityVersion < 1
                || generatedHandler.SemanticAuthorityChecksum.Length != SHA256.HashSizeInBytes
                || generatedHandler.Checksum.Length != SHA256.HashSizeInBytes
                || generatedHandler.InputTypeId != value.InputTypeId || generatedHandler.ResultTypeId != value.ResultTypeId)
                throw new InvalidOperationException("base.activation.definitionInvalid");
        }
        ValidateTarget(value.TransactionalTarget);
        long receiptLifetimeTicks = value.ReceiptRetention.DuplicateResolutionLifetime.Ticks;
        if (value.ReceiptRetention.FormatVersion != 1
            || !Enum.IsDefined(value.ReceiptRetention.ProtectedBackupCoverage)
            || receiptLifetimeTicks % TimeSpan.TicksPerMillisecond != 0
            || value.ReceiptRetention.DuplicateResolutionLifetime < TimeSpan.FromHours(1)
            || value.ReceiptRetention.DuplicateResolutionLifetime > TimeSpan.FromDays(90)
            || value.Retry.MaximumAttempts is < 1 or > 1024 || value.Limits.MaximumAttempts != value.Retry.MaximumAttempts ||
            value.Limits.MaximumInputBytes is < 1 or > 4L * 1024 * 1024 || value.Limits.MaximumResultBytes is < 1 or > 4L * 1024 * 1024 ||
            value.Limits.MaximumYields is < 0 or > 1_000_000 ||
            value.ExecutionClass != BaseActivationExecutionClass.AtLeastOnceWorker && value.Limits.MaximumYields != 0 ||
            value.Limits.MaximumRenewalsPerSlice is < 1 or > 4096 || value.Limits.MaximumChildrenPerSlice is < 1 or > 4096 ||
            value.Limits.HandlerTimeout <= TimeSpan.Zero || value.Limits.HandlerTimeout > TimeSpan.FromHours(24) ||
            value.Retry.InitialDelayMilliseconds < 0 || value.Retry.MaximumDelayMilliseconds < value.Retry.InitialDelayMilliseconds ||
            value.Retry.MultiplierNumerator <= 0 || value.Retry.MultiplierDenominator <= 0 || value.Retry.JitterBasisPoints is < 0 or > 10_000)
            throw new InvalidOperationException("base.activation.definitionInvalid");
    }

    private static byte[] ComputeChecksum(BaseActivationDefinition value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        bool generated = !value.DtoAuthorityChecksum.IsDefaultOrEmpty;
        Append(hash, generated ? "base.activation.definition.v3\0" : "base.activation.definition.v2\0"); Append(hash, value.Id); Append(hash, value.Version);
        Append(hash, value.OwningModuleId); Append(hash, (int)value.ExecutionClass); Append(hash, value.InputTypeId); Append(hash, value.ResultTypeId);
        if (generated)
        {
            Append(hash, value.InputDtoAuthorityChecksum.AsSpan()); Append(hash, value.ResultDtoAuthorityChecksum.AsSpan());
            Append(hash, value.DtoAuthorityChecksum.AsSpan());
        }
        Append(hash, value.InputDisclosureChecksum.AsSpan()); Append(hash, value.ResultDisclosureChecksum.AsSpan());
        Append(hash, value.Grants.Enqueue); Append(hash, value.Grants.Observe); Append(hash, value.Grants.Claim);
        Append(hash, value.Grants.Execute); Append(hash, value.Grants.Renew); Append(hash, value.Grants.Complete);
        Append(hash, value.Grants.Fail); Append(hash, value.Grants.Yield); Append(hash, value.Grants.Cancel); Append(hash, value.Grants.Inspect);
        Append(hash, value.Grants.Replay); Append(hash, value.Grants.Migrate); Append(hash, value.Grants.Reconcile); Append(hash, value.Grants.Retry);
        Append(hash, value.Grants.Dispose); Append(hash, value.Grants.Remove); Append(hash, value.Grants.Repair);
        foreach (string grant in value.SourceGrantIds) Append(hash, grant);
        Append(hash, value.Retry.MaximumAttempts); Append(hash, value.Retry.InitialDelayMilliseconds); Append(hash, value.Retry.MaximumDelayMilliseconds);
        Append(hash, value.Retry.MultiplierNumerator); Append(hash, value.Retry.MultiplierDenominator); Append(hash, value.Retry.JitterBasisPoints);
        foreach (string code in value.Retry.RetryableFailureCodes) Append(hash, code);
        Append(hash, value.ReceiptRetention.FormatVersion);
        Append(hash, value.ReceiptRetention.DuplicateResolutionLifetime.Ticks);
        Append(hash, (int)value.ReceiptRetention.ProtectedBackupCoverage);
        Append(hash, value.Limits.MaximumInputBytes); Append(hash, value.Limits.MaximumResultBytes); Append(hash, value.Limits.MaximumAttempts);
        Append(hash, value.Limits.MaximumYields);
        Append(hash, value.Limits.MaximumRenewalsPerSlice); Append(hash, value.Limits.MaximumChildrenPerSlice); Append(hash, value.Limits.MaximumLineageDepth);
        Append(hash, value.Limits.LeaseDuration.Ticks); Append(hash, value.Limits.HandlerTimeout.Ticks);
        if (generated) { AppendProviderLimits(hash, value.Limits.Provider); AppendAtomicLimits(hash, value.Limits.AtomicCreation); }
        if (value.Handler is not null)
        {
            Append(hash, value.Handler.Id); Append(hash, value.Handler.Version); Append(hash, value.Handler.FactoryId);
            if (generated)
            {
                Append(hash, value.Handler.InputTypeId); Append(hash, value.Handler.ResultTypeId); Append(hash, (int)value.Handler.WorkerSubjectKind);
                Append(hash, value.Handler.SemanticAuthorityId); Append(hash, value.Handler.SemanticAuthorityVersion);
                Append(hash, value.Handler.SemanticAuthorityChecksum.AsSpan());
            }
            Append(hash, value.Handler.Checksum.AsSpan());
        }
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

    private static void AppendProviderLimits(IncrementalHash hash, BaseActivationExecutionLimits value)
    {
        Append(hash, value.MaximumCandidates); Append(hash, value.MaximumInputBytes); Append(hash, value.MaximumResultBytes);
        Append(hash, value.MaximumEvidenceBytes); Append(hash, value.MaximumTransientBytes); Append(hash, value.MaximumReadIntervals);
        Append(hash, value.MaximumIndexOperations); Append(hash, value.AcquisitionTimeout.Ticks); Append(hash, value.TransactionTimeout.Ticks);
        Append(hash, value.CommitObservationTimeout.Ticks); Append(hash, value.ReceiptResolutionTimeout.Ticks);
    }

    private static void AppendAtomicLimits(IncrementalHash hash, BaseAtomicMutationExecutionLimits value)
    {
        Append(hash, value.Schema is null ? 0 : 1);
        if (value.Schema is { } schema)
        {
            Append(hash, schema.MaximumRecords); Append(hash, schema.MaximumCanonicalBytes); Append(hash, schema.MaximumJsonNodes);
            Append(hash, schema.MaximumConstraintEvaluations); Append(hash, schema.MaximumPredicateEvaluations); Append(hash, schema.MaximumKeys);
            Append(hash, schema.MaximumKeyBytes); Append(hash, schema.MaximumUniqueCandidates); Append(hash, schema.MaximumUniqueChecks);
            Append(hash, schema.MaximumIntervals); Append(hash, schema.MaximumIntervalBytes); Append(hash, schema.MaximumEvidenceBytes);
            Append(hash, schema.MaximumTransientBytes);
        }
        Append(hash, value.MaximumItems); Append(hash, value.MaximumQueryNodes); Append(hash, value.MaximumQueryDepth);
        Append(hash, value.MaximumLiteralValues); Append(hash, value.MaximumSelectedRecords); Append(hash, value.MaximumProducedMutations);
        Append(hash, value.MaximumQueryExecutions); Append(hash, value.MaximumPreviousStateRequirements); Append(hash, value.MaximumRecordCaptures);
        Append(hash, value.MaximumRelationTargetCaptures); Append(hash, value.MaximumGenerationReads); Append(hash, value.MaximumGenerationComparisons);
        Append(hash, value.MaximumGenerationIncrements); Append(hash, value.MaximumGuardNodes); Append(hash, value.MaximumGuardDepth);
        Append(hash, value.MaximumStatements); Append(hash, value.MaximumBranches); Append(hash, value.MaximumExpressionNodes);
        Append(hash, value.MaximumSelectedBytes); Append(hash, value.MaximumEvidenceBytes); Append(hash, value.MaximumTransientBytes);
        Append(hash, value.MaximumReadIntervals); Append(hash, value.MaximumSubjectValidations); Append(hash, value.MaximumAuthorityReads);
        Append(hash, value.MaximumRelationChecks); Append(hash, value.MaximumUniqueConstraintChecks); Append(hash, value.MaximumRetirementProjections);
        Append(hash, value.MaximumRetirementBarrierReads); Append(hash, value.MaximumRetirementAcknowledgementReads);
        Append(hash, value.MaximumRetirementPublications); Append(hash, value.MaximumRequestBytes); Append(hash, value.MaximumGenerationBytes);
        Append(hash, value.MaximumWrittenBytes); Append(hash, value.MaximumFactBytes); Append(hash, value.MaximumJournalBytes);
        Append(hash, value.MaximumReceiptBytes); Append(hash, value.MaximumResultBytes); Append(hash, value.MaximumRetirementEvidenceBytes);
        Append(hash, value.MaximumRetirementPublicationBytes); Append(hash, value.Deadlines.AcquisitionTimeout.Ticks);
        Append(hash, value.Deadlines.TransactionTimeout.Ticks); Append(hash, value.Deadlines.CommitObservationTimeout.Ticks);
        Append(hash, value.Deadlines.ReceiptResolutionTimeout.Ticks);
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
            Fail = new string(value.Fail.AsSpan()), Yield = new string(value.Yield.AsSpan()), Cancel = new string(value.Cancel.AsSpan()),
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
        BaseApplicationId.Validate(value.Fail, nameof(value.Fail)); BaseApplicationId.Validate(value.Yield, nameof(value.Yield)); BaseApplicationId.Validate(value.Cancel, nameof(value.Cancel));
        BaseApplicationId.Validate(value.Inspect, nameof(value.Inspect)); BaseApplicationId.Validate(value.Replay, nameof(value.Replay));
        BaseApplicationId.Validate(value.Migrate, nameof(value.Migrate)); BaseApplicationId.Validate(value.Reconcile, nameof(value.Reconcile));
        BaseApplicationId.Validate(value.Retry, nameof(value.Retry));
        BaseApplicationId.Validate(value.Dispose, nameof(value.Dispose)); BaseApplicationId.Validate(value.Remove, nameof(value.Remove));
        BaseApplicationId.Validate(value.Repair, nameof(value.Repair));
    }
}
