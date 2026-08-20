
namespace HPD.Base;

/// <summary>Classifies the confirmed provider outcome of one mutation execution boundary.</summary>
public enum RecordMutationExecutionOutcome
{
    /// <summary>The provider confirmed commit.</summary>
Committed,
    /// <summary>The provider confirmed rollback.</summary>
RollbackConfirmed,
    /// <summary>The provider confirmed rollback after cancellation.</summary>
CancelledRollbackConfirmed,
    /// <summary>The provider confirmed rollback after a transaction conflict.</summary>
ConflictRollbackConfirmed,
    /// <summary>The provider cannot determine whether commit occurred.</summary>
Indeterminate
}

/// <summary>Classifies whether the framework-owned processor permits provider commit.</summary>
public enum AtomicMutationProcessingOutcome
{
    /// <summary>Processing completed successfully and the provider may commit.</summary>
ReadyToCommit,
    /// <summary>Processing failed and the provider must roll back.</summary>
Failed
}

/// <summary>Supplies bounded provider execution lifetimes for one mutation boundary.</summary>
public sealed record RecordMutationExecutionRequest
{
    /// <summary>Gets the maximum duration allowed to acquire the provider boundary.</summary>
    public required TimeSpan AcquisitionTimeout { get; init; }

    /// <summary>Gets the maximum duration allowed for transactional processing.</summary>
    public required TimeSpan TransactionTimeout { get; init; }

    /// <summary>Gets the internal maximum duration allowed to classify commit completion.</summary>
    public required TimeSpan CommitCompletionTimeout { get; init; }

    /// <summary>Gets the identified atomic request contract, when durable resolution is requested.</summary>
    public BaseAtomicMutationExecutionRequest? AtomicRequest { get; init; }
}

/// <summary>Supplies provider-neutral receipt identity and bounds for one atomic execution.</summary>
public sealed record BaseAtomicMutationExecutionRequest
{
    /// <summary>Gets the normalized request identity.</summary>
    public required BaseMutationRequestIdentity Identity { get; init; }
    /// <summary>Gets BASE's canonical 256-bit batch-structure digest.</summary>
    public required byte[] StructuralDigest { get; init; }
    /// <summary>Gets the provider time at which the receipt becomes semantically expired.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
    /// <summary>Gets the maximum canonical receipt size.</summary>
    public required int MaxReceiptBytes { get; init; }
}

/// <summary>Returns the fixed decision produced by the framework-owned mutation processor.</summary>
public sealed record AtomicMutationProcessingResult
{
    /// <summary>Initializes a processor decision and enforces its error invariant.</summary>
    /// <param name="outcome">Whether the provider may commit or must roll back.</param>
    /// <param name="mutations">Canonical provisional mutation facts in execution order.</param>
    /// <param name="error">A bounded error required only for a failed decision.</param>
    /// <exception cref="ArgumentException">The outcome and error do not form a valid decision.</exception>
    public AtomicMutationProcessingResult(
        AtomicMutationProcessingOutcome outcome,
        BaseRecordMutationFact[] mutations,
        BaseError? error = null)
        : this(outcome, BaseAtomicReceiptResult.FromFacts(mutations), error)
    {
    }

    /// <summary>Initializes a processor decision with one closed deeply owned receipt envelope.</summary>
    public AtomicMutationProcessingResult(
        AtomicMutationProcessingOutcome outcome,
        BaseAtomicReceiptResult receipt,
        BaseError? error = null)
        : this(outcome, receipt, null, error)
    {
    }

    /// <summary>Initializes a ready decision with Runtime-owned pre-commit finalization authority.</summary>
    public AtomicMutationProcessingResult(BaseAtomicMutationCommitFinalization finalization)
        : this(AtomicMutationProcessingOutcome.ReadyToCommit, finalization?.Receipt!, finalization, null)
    {
        ArgumentNullException.ThrowIfNull(finalization);
    }

    private AtomicMutationProcessingResult(
        AtomicMutationProcessingOutcome outcome,
        BaseAtomicReceiptResult receipt,
        BaseAtomicMutationCommitFinalization? finalization,
        BaseError? error)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (outcome == AtomicMutationProcessingOutcome.ReadyToCommit && error is not null)
            throw new ArgumentException("A ready-to-commit result cannot carry an error.", nameof(error));
        if (outcome == AtomicMutationProcessingOutcome.Failed && error is null)
            throw new ArgumentException("A failed processing result requires a bounded error.", nameof(error));

        Outcome = outcome;
        Receipt = receipt;
        Finalization = finalization;
        Mutations = receipt.MaterializeFacts();
        Error = error;
    }

    /// <summary>Gets whether the provider may commit or must roll back.</summary>
    public AtomicMutationProcessingOutcome Outcome { get; }

    /// <summary>Gets the bounded normalized processing failure, when processing failed.</summary>
    public BaseError? Error { get; }

    /// <summary>
    /// Gets the canonical provisional mutation facts in execution order. Session primitives
    /// perform any transactional journal append with the physical mutation; this collection
    /// controls commit classification and later result/event processing.
    /// </summary>
    public BaseRecordMutationFact[] Mutations { get; }

    /// <summary>Gets the closed deeply owned result persisted for identified requests.</summary>
    public BaseAtomicReceiptResult Receipt { get; }

    /// <summary>Gets Runtime-owned result, receipt, and aggregate accounting for pre-commit validation.</summary>
    public BaseAtomicMutationCommitFinalization? Finalization { get; }
}

/// <summary>
/// Carries one canonical provisional mutation fact across the Runtime/provider boundary.
/// The fact is not a public response and becomes committed only after provider confirmation.
/// </summary>
public sealed record BaseRecordMutationFact
{
    /// <summary>Gets the optional batch item handle associated with the mutation.</summary>
    public string? ItemId { get; init; }

    /// <summary>Gets the operation requested by Runtime, including logical upsert.</summary>
    public required BaseRecordMutationKind RequestedOperation { get; init; }

    /// <summary>Gets the physical create, patch, replace, or delete operation used for journaling.</summary>
    public required BaseCommittedRecordMutationKind CommittedOperation { get; init; }

    /// <summary>Gets the logical upsert branch when the requested operation was upsert.</summary>
    public RecordUpsertOutcome? UpsertOutcome { get; init; }

    /// <summary>Gets the target collection definition.</summary>
    public required CollectionDefinition Collection { get; init; }

    /// <summary>
    /// Gets the canonical committed-event reference produced with the physical mutation.
    /// Runtime uses this fact directly after commit and never recovers journal identity from
    /// operation-result metadata.
    /// </summary>
    public required EventReference Event { get; init; }

    /// <summary>Gets the provider-local provisional mutation-journal position.</summary>
    public BaseMutationJournalPosition JournalPosition { get; init; }

    /// <summary>Gets the unredacted record state before the mutation, when applicable.</summary>
    public RecordEnvelope? Before { get; init; }

    /// <summary>Gets the unredacted record state after the mutation, when applicable.</summary>
    public RecordEnvelope? After { get; init; }

    /// <summary>Gets the physical delete result when the committed operation is delete.</summary>
    public DeleteResult? Delete { get; init; }

    /// <summary>Gets the bounded changed-field names, when available.</summary>
    public string[]? ChangedFields { get; init; }

    /// <summary>Gets provider-allocated exported-subject lifecycle evidence when this mutation owns a subject lifetime.</summary>
    public BaseSubjectLifecycleCommitEvidence? SubjectLifecycle { get; init; }
}

/// <summary>Contains immutable committed evidence for one exported-subject lifetime transition.</summary>
public sealed record BaseSubjectLifecycleCommitEvidence
{
    /// <summary>Gets the exported contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the exported contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the canonical logical subject identity text.</summary>
    public required string SubjectId { get; init; }
    /// <summary>Gets the committed lifecycle transition.</summary>
    public required BaseSubjectLifecycleMutationKind Kind { get; init; }
    /// <summary>Gets the exact authority epoch that owns the committed lifetime.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the exact committed incarnation, including terminal retirement.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
    /// <summary>Gets the positive subject-local sequence after the transition.</summary>
    public required long SubjectSequence { get; init; }
    /// <summary>Gets the contract state generation that authorized publication.</summary>
    public required long ContractStateGeneration { get; init; }
    /// <summary>Gets the lifecycle delivery epoch.</summary>
    public required long DeliveryEpoch { get; init; }
    /// <summary>Gets the exact logical tenant/project scope that owns this lifecycle transition.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the prior lifecycle state, or null for creation.</summary>
    public BaseSubjectLifecycleState? PreviousState { get; init; }
    /// <summary>Gets the resulting lifecycle state.</summary>
    public required BaseSubjectLifecycleState ResultingState { get; init; }
    /// <summary>Gets the exact committed mutation-journal position.</summary>
    public required BaseMutationJournalPosition CommitPosition { get; init; }
}

/// <summary>Supplies Runtime-owned identity and context to one physical session mutation.</summary>
public sealed record RecordMutationSessionContext
{
    /// <summary>Gets the optional batch item handle.</summary>
    public string? ItemId { get; init; }

    /// <summary>Gets the logical requested operation, including upsert.</summary>
    public required BaseRecordMutationKind RequestedOperation { get; init; }

    /// <summary>Gets the stable identity used by a transactional mutation journal.</summary>
    public required string EventId { get; init; }

    /// <summary>Gets the normalized Runtime operation context.</summary>
    public required OperationContext Operation { get; init; }

    /// <summary>
    /// Gets the authoritative Runtime-computed changed fields for the physical mutation.
    /// Providers preserve this metadata and do not reconstruct it from stored payloads.
    /// </summary>
    public string[]? ChangedFields { get; init; }
}

/// <summary>Returns one physical session mutation and its canonical provisional fact.</summary>
public sealed record RecordMutationSessionResult
{
    /// <summary>Gets the canonical provisional mutation fact.</summary>
    public required BaseRecordMutationFact Mutation { get; init; }

    /// <summary>Gets the provisional record for create, patch, or replace.</summary>
    public RecordEnvelope? Record { get; init; }

    /// <summary>Gets the provisional delete result for delete.</summary>
    public DeleteResult? Delete { get; init; }
}

/// <summary>Returns the fixed provider commit classification for a mutation execution.</summary>
public sealed record RecordMutationExecutionResult
{
    /// <summary>Initializes and validates one provider commit classification.</summary>
    /// <param name="outcome">The confirmed or indeterminate provider outcome.</param>
    /// <param name="processing">The processor decision, omitted for an indeterminate outcome.</param>
    /// <param name="error">The bounded provider error, when applicable.</param>
    /// <exception cref="ArgumentException">The outcome and processor decision are inconsistent.</exception>
    public RecordMutationExecutionResult(
        RecordMutationExecutionOutcome outcome,
        AtomicMutationProcessingResult? processing,
        BaseError? error = null)
    {
        if (outcome == RecordMutationExecutionOutcome.Committed
            && processing?.Outcome != AtomicMutationProcessingOutcome.ReadyToCommit)
        {
            throw new ArgumentException("A committed execution requires a ready-to-commit processor result.", nameof(processing));
        }

        if (outcome == RecordMutationExecutionOutcome.Indeterminate && processing is not null)
            throw new ArgumentException("An indeterminate execution cannot expose provisional mutations.", nameof(processing));

        Outcome = outcome;
        Processing = processing;
        Error = error;
    }

    /// <summary>Gets the confirmed or indeterminate provider outcome.</summary>
    public RecordMutationExecutionOutcome Outcome { get; }

    /// <summary>Gets the processor decision returned inside the provider boundary.</summary>
    public AtomicMutationProcessingResult? Processing { get; }

    /// <summary>Gets a bounded normalized provider failure.</summary>
    public BaseError? Error { get; }

    /// <summary>Gets whether commit was new or resolved from an existing receipt.</summary>
    public BaseMutationRequestDisposition RequestDisposition { get; init; } = BaseMutationRequestDisposition.Committed;
}

/// <summary>
/// Provides the only ordinary provider mutation entry point. Implementations create one
/// restricted session and invoke the supplied framework processor exactly once.
/// </summary>
public interface IRecordMutationStore : IRecordStore
{
    /// <summary>Executes one command processor in one provider-owned mutation boundary.</summary>
    /// <param name="processor">The fixed framework-owned processor to invoke.</param>
    /// <param name="request">The bounded execution lifetimes.</param>
    /// <param name="cancellationToken">Cancellation requested before confirmed commit.</param>
    /// <returns>The provider's fixed commit classification.</returns>
    ValueTask<RecordMutationExecutionResult> ExecuteSingleAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Provides a real grouped atomic mutation guarantee over one store instance.</summary>
public interface IAtomicRecordStore : IRecordMutationStore
{
    /// <summary>Resolves one stored identified receipt without recapturing or re-executing mutation authority.</summary>
    ValueTask<RecordMutationExecutionResult> ResolveAtomicReceiptAsync(
        IAtomicMutationProcessor processor,
        BaseMutationRequestIdentity identity,
        TimeSpan resolutionTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>Captures one coherent authority requirement for the exact collection set.</summary>
    ValueTask<OperationResult<BaseAtomicMutationAuthorityRequirement>> CaptureAtomicMutationAuthorityRequirementAsync(
        string applicationId,
        System.Collections.Immutable.ImmutableArray<CollectionDefinition> collections,
        BaseAtomicMutationExecutionLimits limits,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(OperationResults.Unsupported<BaseAtomicMutationAuthorityRequirement>(new BaseError
        {
            Code = "base.atomic.authorityUnavailable",
            Message = "Atomic mutation authority is unavailable.",
            Category = ErrorCategory.Unsupported,
        }));

    /// <summary>Executes the supplied processor in one provider-owned atomic transaction.</summary>
    /// <param name="processor">The fixed framework-owned processor to invoke.</param>
    /// <param name="request">The bounded execution lifetimes.</param>
    /// <param name="cancellationToken">Cancellation requested before confirmed commit.</param>
    /// <returns>The provider's fixed commit classification.</returns>
    ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a restricted provider-neutral record view valid only while its provider invokes
/// an <see cref="IAtomicMutationProcessor"/>.
/// </summary>
public interface IAtomicRecordSession
{
    /// <summary>Captures immutable current-state authority for one canonical caller-semantic intent.</summary>
    ValueTask<OperationResult<BaseCapturedAtomicMutationAuthority>> CaptureAtomicMutationAuthorityAsync(
        BaseAtomicMutationCaptureRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Prepares final dispositions, constraints, lifecycle overlay, and subject validation without applying writes.</summary>
    ValueTask<OperationResult<BasePreparedAtomicMutation>> PrepareAtomicMutationAsync(
        BaseCapturedAtomicMutationAuthority captured,
        BaseAtomicMutationPlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>Consumes one exact session-bound preparation and applies all canonical artifacts atomically.</summary>
    ValueTask<OperationResult<BaseProvisionalAppliedAtomicMutation>> ApplyPreparedAtomicMutationAsync(
        BasePreparedAtomicMutation prepared,
        CancellationToken cancellationToken = default);

    /// <summary>Measures exact canonical and durable artifacts produced by the current transaction.</summary>
    ValueTask<OperationResult<BaseSelectionMutationCommitAccounting>> MeasureSelectionMutationAsync(
        BaseAtomicReceiptResult receipt,
        BaseSelectionMutationResult result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(OperationResults.Unsupported<BaseSelectionMutationCommitAccounting>(new BaseError
        {
            Code = "base.provider.selection.accountingUnavailable",
            Message = "This provider cannot certify selection mutation accounting.",
            Category = ErrorCategory.Unsupported,
        }));

    /// <summary>Reads one record from the transaction-bound view.</summary>
    ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Creates one record in the transaction-bound view.</summary>
    ValueTask<OperationResult<RecordMutationSessionResult>> CreateAsync(
        CollectionDefinition collection,
        RecordCreateRequest request,
        RecordMutationSessionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Patches one record in the transaction-bound view.</summary>
    ValueTask<OperationResult<RecordMutationSessionResult>> PatchAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        RecordMutationSessionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces one record in the transaction-bound view.</summary>
    ValueTask<OperationResult<RecordMutationSessionResult>> ReplaceAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        RecordMutationSessionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes one record in the transaction-bound view.</summary>
    ValueTask<OperationResult<RecordMutationSessionResult>> DeleteAsync(
        CollectionDefinition collection,
        RecordId id,
        RecordDeleteRequest request,
        RecordMutationSessionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Advances one purge-enabled collection generation inside this transaction.</summary>
    ValueTask<OperationResult<long>> AdvancePurgeGenerationAsync(
        CollectionDefinition collection,
        long? expectedGeneration,
        CancellationToken cancellationToken = default);

    /// <summary>Advances one provider-owned durable subject-lifecycle checkpoint in this transaction.</summary>
    ValueTask<OperationResult<BaseSubjectLifecycleCheckpointResult>> AdvanceSubjectLifecycleCheckpointAsync(
        BaseSubjectLifecycleProviderCheckpointRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one Runtime-authorized subject-retirement acknowledgement.</summary>
    ValueTask<OperationResult<BaseSubjectAcknowledgementResult>> ApplySubjectRetirementAcknowledgementAsync(
        BaseSubjectRetirementProviderAcknowledgementRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Applies one Runtime-authorized retirement timeout transition.</summary>
    ValueTask<OperationResult<BaseSubjectRetirementTimeoutResult>> ApplySubjectRetirementTimeoutAsync(
        BaseSubjectRetirementProviderTimeoutRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(OperationResults.Unsupported<BaseSubjectRetirementTimeoutResult>(new BaseError { Code = BaseSubjectRetirementErrorCodes.ProviderContractInvalid, Message = "The provider does not support retirement timeout processing.", Category = ErrorCategory.Unsupported }));

    /// <summary>Applies one Runtime-authorized retirement override transition.</summary>
    ValueTask<OperationResult<BaseSubjectRetirementOverrideResult>> ApplySubjectRetirementOverrideAsync(
        BaseSubjectRetirementProviderOverrideRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(OperationResults.Unsupported<BaseSubjectRetirementOverrideResult>(new BaseError { Code = BaseSubjectRetirementErrorCodes.ProviderContractInvalid, Message = "The provider does not support retirement override.", Category = ErrorCategory.Unsupported }));

    /// <summary>Applies one Runtime-authorized final physical purge.</summary>
    ValueTask<OperationResult<BaseSubjectRetirementPurgeApplied>> ApplySubjectRetirementPurgeAsync(
        BaseSubjectRetirementProviderPurgeRequest request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(OperationResults.Unsupported<BaseSubjectRetirementPurgeApplied>(new BaseError { Code = BaseSubjectRetirementErrorCodes.ProviderContractInvalid, Message = "The provider does not support final subject purge.", Category = ErrorCategory.Unsupported }));

    /// <summary>Applies every installed provider-owned projection inside this transaction.</summary>
    /// <param name="request">The deeply immutable canonical projection facts.</param>
    /// <param name="cancellationToken">Cancellation requested before confirmed commit.</param>
    /// <returns>A bounded success or failure result.</returns>
    ValueTask<OperationResult> ApplyMutationProjectionsAsync(
        BaseAtomicMutationProjectionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Processes canonical Runtime mutations against a provider-owned restricted session.</summary>
public interface IAtomicMutationProcessor
{
    /// <summary>Processes all requested mutations and determines whether the provider may commit.</summary>
    /// <param name="session">The restricted transaction-bound record session.</param>
    /// <param name="cancellationToken">Cancellation requested before confirmed commit.</param>
    /// <returns>The fixed commit-or-rollback decision.</returns>
    ValueTask<AtomicMutationProcessingResult> ProcessAsync(
        IAtomicRecordSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Authorizes and projects one bounded stored receipt before fingerprint disclosure.</summary>
    ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseRecordMutationFact[] committedMutations,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.Failed,
            [],
            new BaseError
            {
                Code = BaseMutationRequestErrorCodes.ReceiptUnavailable,
                Message = "The stored mutation receipt cannot be resolved.",
                Category = ErrorCategory.Authorization,
            }));

    /// <summary>Authorizes and projects one closed stored receipt result.</summary>
    ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
        BaseAtomicReceiptResult committedResult,
        CancellationToken cancellationToken = default) =>
        ResolveReceiptAsync(committedResult.MaterializeFacts(), cancellationToken);
}
