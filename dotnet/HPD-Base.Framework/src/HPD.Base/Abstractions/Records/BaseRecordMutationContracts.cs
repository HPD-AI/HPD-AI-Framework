
namespace HPD.Base;

/// <summary>Identifies one supported record mutation command.</summary>
public enum BaseRecordMutationKind
{
    /// <summary>Create a record.</summary>
Create,
    /// <summary>Patch an existing record.</summary>
Patch,
    /// <summary>Replace an existing record.</summary>
Replace,
    /// <summary>Delete an existing record.</summary>
Delete,
    /// <summary>Atomically create or update a record by its record identifier.</summary>
Upsert
}

/// <summary>Identifies the physical record mutation that committed at the provider.</summary>
public enum BaseCommittedRecordMutationKind
{
    /// <summary>A record was physically created.</summary>
Create,
    /// <summary>An existing record was physically patched.</summary>
Patch,
    /// <summary>An existing record was physically replaced.</summary>
Replace,
    /// <summary>An existing record was physically deleted.</summary>
Delete
}

/// <summary>Controls ordered batch execution and its commit guarantees.</summary>
public enum BaseRecordBatchExecutionMode
{
    /// <summary>Execute every item in order as an independent commit.</summary>
OrderedIndependent,
    /// <summary>Execute independent commits in order and stop after the first failure.</summary>
OrderedStopOnFailure,
    /// <summary>Execute every item in one provider-owned atomic transaction.</summary>
Atomic
}

/// <summary>Describes the aggregate outcome of a record batch.</summary>
public enum BaseRecordBatchOutcome
{
    /// <summary>Every requested mutation committed.</summary>
Committed,
    /// <summary>At least one mutation committed and at least one did not commit.</summary>
PartiallyCommitted,
    /// <summary>The provider confirmed that the atomic batch was rolled back.</summary>
RolledBack,
    /// <summary>No mutation committed and execution failed without an atomic rollback result.</summary>
Failed
}

/// <summary>Describes the transaction disposition of one batch item.</summary>
public enum BaseRecordBatchItemDisposition
{
    /// <summary>The item committed.</summary>
Committed,
    /// <summary>The item executed and failed.</summary>
Failed,
    /// <summary>The item was not executed.</summary>
Skipped,
    /// <summary>The item executed provisionally and the provider confirmed rollback.</summary>
RolledBack
}

/// <summary>Selects how the update branch of an upsert modifies an existing record.</summary>
public enum RecordUpsertUpdateMode
{
    /// <summary>Merge supplied top-level fields into the existing payload.</summary>
Patch,
    /// <summary>Replace the complete existing payload.</summary>
Replace
}

/// <summary>Constrains which existence branch an upsert may take.</summary>
public enum RecordUpsertExistenceCondition
{
    /// <summary>Create when absent or update when present.</summary>
Any,
    /// <summary>Create only and conflict when the record already exists.</summary>
CreateOnly,
    /// <summary>Update only and return not found when the record is absent.</summary>
UpdateOnly
}

/// <summary>Reports which branch of an atomic upsert committed.</summary>
public enum RecordUpsertOutcome
{
    /// <summary>The record was created.</summary>
Created,
    /// <summary>The existing record was updated.</summary>
Updated
}

/// <summary>Requests an atomic record-ID-keyed create-or-update operation.</summary>
public sealed record RecordUpsertRequest
{
    /// <summary>Gets the stable record identifier used for atomic branch selection.</summary>
    public required RecordId Id { get; init; }

    /// <summary>Gets the complete payload used by the create branch.</summary>
    public required RecordPayload CreatePayload { get; init; }

    /// <summary>Gets the payload used by the update branch.</summary>
    public required RecordPayload UpdatePayload { get; init; }

    /// <summary>Gets whether the update branch patches or replaces the existing payload.</summary>
    public required RecordUpsertUpdateMode UpdateMode { get; init; }

    /// <summary>Gets the permitted existence branch.</summary>
    public required RecordUpsertExistenceCondition Condition { get; init; }

    /// <summary>Gets the optional expected revision for the update branch.</summary>
    public RevisionToken? ExpectedRevision { get; init; }
}

/// <summary>Returns the committed branch and record from an atomic upsert.</summary>
public sealed record RecordUpsertResult
{
    /// <summary>Gets the branch that committed.</summary>
    public required RecordUpsertOutcome Outcome { get; init; }

    /// <summary>Gets the committed record.</summary>
    public required RecordEnvelope Record { get; init; }
}

/// <summary>Requests a bounded ordered set of record mutations.</summary>
public sealed record BaseRecordBatchRequest
{
    /// <summary>Gets the required execution mode.</summary>
    public required BaseRecordBatchExecutionMode Mode { get; init; }

    /// <summary>Gets the mutations in execution order.</summary>
    public required BaseRecordBatchItem[] Operations { get; init; }

    /// <summary>Gets the durable identity for an atomic request, when requested.</summary>
    public BaseMutationRequestIdentity? RequestIdentity { get; init; }
}

/// <summary>Describes one closed, typed mutation in a record batch.</summary>
public sealed record BaseRecordBatchItem
{
    /// <summary>Gets the bounded caller correlation handle unique within the batch.</summary>
    public required string ItemId { get; init; }

    /// <summary>Gets the target collection identifier.</summary>
    public required string CollectionId { get; init; }

    /// <summary>Gets the mutation discriminator.</summary>
    public required BaseRecordMutationKind Kind { get; init; }

    /// <summary>Gets the create request when <see cref="Kind"/> is <see cref="BaseRecordMutationKind.Create"/>.</summary>
    public RecordCreateRequest? Create { get; init; }

    /// <summary>Gets the target record identifier for patch, replace, or delete.</summary>
    public RecordId? RecordId { get; init; }

    /// <summary>Gets the patch request when <see cref="Kind"/> is <see cref="BaseRecordMutationKind.Patch"/>.</summary>
    public RecordPatchRequest? Patch { get; init; }

    /// <summary>Gets the replace request when <see cref="Kind"/> is <see cref="BaseRecordMutationKind.Replace"/>.</summary>
    public RecordReplaceRequest? Replace { get; init; }

    /// <summary>Gets the delete request when <see cref="Kind"/> is <see cref="BaseRecordMutationKind.Delete"/>.</summary>
    public RecordDeleteRequest? Delete { get; init; }

    /// <summary>Gets the upsert request when <see cref="Kind"/> is <see cref="BaseRecordMutationKind.Upsert"/>.</summary>
    public RecordUpsertRequest? Upsert { get; init; }
}

/// <summary>Returns the ordered result of a record batch.</summary>
public sealed record BaseRecordBatchResult
{
    /// <summary>Gets the aggregate batch outcome.</summary>
    public required BaseRecordBatchOutcome Outcome { get; init; }

    /// <summary>Gets one result for every requested item in request order.</summary>
    public required BaseRecordBatchItemResult[] Items { get; init; }

    /// <summary>
    /// Gets a bounded aggregate failure when the provider rejected the transaction
    /// without attributing failure to one command.
    /// </summary>
    public BaseError? Error { get; init; }

    /// <summary>Gets the bounded number of post-commit warnings across committed items.</summary>
    public int PostCommitWarningCount { get; init; }

    /// <summary>Gets whether this result newly committed or resolved a prior commit.</summary>
    public BaseMutationRequestDisposition RequestDisposition { get; init; } =
        BaseMutationRequestDisposition.Committed;
}

/// <summary>Returns the normalized result and transaction disposition of one batch item.</summary>
public sealed record BaseRecordBatchItemResult
{
    /// <summary>Gets the caller-supplied batch item handle.</summary>
    public required string ItemId { get; init; }

    /// <summary>Gets the zero-based request and execution position.</summary>
    public required int Index { get; init; }

    /// <summary>Gets the requested mutation kind.</summary>
    public required BaseRecordMutationKind Kind { get; init; }

    /// <summary>Gets whether the item committed, failed, was skipped, or rolled back.</summary>
    public required BaseRecordBatchItemDisposition Disposition { get; init; }

    /// <summary>Gets the normalized operation status.</summary>
    public required OperationStatus Status { get; init; }

    /// <summary>Gets the committed record for create, patch, or replace.</summary>
    public RecordEnvelope? Record { get; init; }

    /// <summary>Gets the committed delete result.</summary>
    public DeleteResult? Delete { get; init; }

    /// <summary>Gets the committed upsert result.</summary>
    public RecordUpsertResult? Upsert { get; init; }

    /// <summary>Gets the bounded normalized error for a failed item.</summary>
    public BaseError? Error { get; init; }

    /// <summary>Gets bounded warnings associated with this committed item.</summary>
    public OperationWarning[]? Warnings { get; init; }

    /// <summary>Gets committed revision metadata.</summary>
    public RevisionInfo? Revision { get; init; }

    /// <summary>Gets committed event references.</summary>
    public EventReference[]? Events { get; init; }
}
