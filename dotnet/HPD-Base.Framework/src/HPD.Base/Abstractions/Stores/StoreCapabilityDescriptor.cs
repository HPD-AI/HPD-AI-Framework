using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a store capability descriptor.</summary>
public sealed record StoreCapabilityDescriptor
{
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the store kind.</summary>
    public required string StoreKind { get; init; }
    /// <summary>Gets or sets the store version.</summary>
    public required string StoreVersion { get; init; }
    /// <summary>Gets portable record-read support and limits.</summary>
    public required RecordReadCapability Read { get; init; }
    /// <summary>Gets portable record-mutation support and authority.</summary>
    public required RecordMutationCapability Mutation { get; init; }
    /// <summary>Gets or sets the query.</summary>
    public required QueryCapability Query { get; init; }
    /// <summary>Gets or sets the revision.</summary>
    public RevisionCapability? Revision { get; init; }
    /// <summary>Gets ordered and atomic batch guarantees, when implemented.</summary>
    public StoreBatchCapability? Batch { get; init; }
    /// <summary>Gets atomic record-ID upsert guarantees, when implemented.</summary>
    public StoreUpsertCapability? Upsert { get; init; }
    /// <summary>Gets identified atomic-request receipt guarantees.</summary>
    public AtomicRequestCapability? AtomicRequest { get; init; }
    /// <summary>Gets transaction-bound selection-and-mutation guarantees.</summary>
    public BaseSelectionMutationCapability? SelectionMutation { get; init; }
    /// <summary>Gets host-only provider administration guarantees.</summary>
    public BaseAdministrationCapability? Administration { get; init; }
    /// <summary>Gets or sets the streaming.</summary>
    public StreamingCapability? Streaming { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Declares portable read operations and their page bound.</summary>
public sealed record RecordReadCapability
{
    /// <summary>Gets whether list is implemented.</summary>
    public bool List { get; init; }
    /// <summary>Gets whether get-by-ID is implemented.</summary>
    public bool Get { get; init; }
    /// <summary>Gets the provider's maximum page size, when bounded.</summary>
    public int? MaxPageSize { get; init; }
}

/// <summary>Declares physical record mutations and provider authority.</summary>
public sealed record RecordMutationCapability
{
    /// <summary>Gets whether create is implemented.</summary>
    public bool Create { get; init; }
    /// <summary>Gets whether patch is implemented.</summary>
    public bool Patch { get; init; }
    /// <summary>Gets whether replace is implemented.</summary>
    public bool Replace { get; init; }
    /// <summary>Gets whether delete is implemented.</summary>
    public bool Delete { get; init; }
    /// <summary>Gets which layer supplies record identifiers.</summary>
    public IdAuthority IdAuthority { get; init; }
    /// <summary>Gets which layer supplies persisted timestamps.</summary>
    public TimestampAuthority TimestampAuthority { get; init; }
    /// <summary>Gets the provider's consistency classification.</summary>
    public ConsistencyModel Consistency { get; init; }
    /// <summary>Gets the closed collection mutation modes enforced by the provider.</summary>
    public BaseCollectionMutationMode[] MutationModes { get; init; } = [];
    /// <summary>Gets whether the provider supports canonical administrative purge.</summary>
    public bool AdministrativePurge { get; init; }
}

/// <summary>Defines the ID authority contract.</summary>
public enum IdAuthority { /// <summary>Identifies runtime.</summary>
Runtime, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies client.</summary>
Client, /// <summary>Identifies hybrid.</summary>
Hybrid }
/// <summary>Defines the timestamp authority contract.</summary>
public enum TimestampAuthority { /// <summary>Identifies runtime.</summary>
Runtime, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies client.</summary>
Client, /// <summary>Identifies hybrid.</summary>
Hybrid, /// <summary>Identifies none.</summary>
None }
/// <summary>Defines the consistency model contract.</summary>
public enum ConsistencyModel { /// <summary>Identifies strong.</summary>
Strong, /// <summary>Identifies eventual.</summary>
Eventual, /// <summary>Identifies session.</summary>
Session, /// <summary>Identifies store defined.</summary>
StoreDefined }

/// <summary>Declares conditional mutation support and its revision guarantee.</summary>
public sealed record RevisionCapability
{
    /// <summary>Gets whether revision tokens are supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets the enforcement guarantee.</summary>
    public RevisionGuarantee Guarantee { get; init; }
    /// <summary>Gets whether patch enforces an expected revision atomically.</summary>
    public bool Patch { get; init; }
    /// <summary>Gets whether replace enforces an expected revision atomically.</summary>
    public bool Replace { get; init; }
    /// <summary>Gets whether delete enforces an expected revision atomically.</summary>
    public bool Delete { get; init; }
}

/// <summary>Declares the store's ordered batch guarantees and hard limits.</summary>
public sealed record StoreBatchCapability
{
    /// <summary>Gets the supported execution modes.</summary>
    public required BaseRecordBatchExecutionMode[] Modes { get; init; }
    /// <summary>Gets the maximum operations accepted in one batch.</summary>
    public required int MaxOperations { get; init; }
    /// <summary>Gets the maximum canonical serialized payload bytes.</summary>
    public required long MaxCanonicalPayloadBytes { get; init; }
    /// <summary>Gets the provider's minimum supported boundary-acquisition timeout.</summary>
    public required TimeSpan MinimumAcquisitionTimeout { get; init; }
    /// <summary>Gets the provider's minimum supported transactional-processing timeout.</summary>
    public required TimeSpan MinimumTransactionTimeout { get; init; }
    /// <summary>Gets the provider's minimum supported commit-classification timeout.</summary>
    public required TimeSpan MinimumCommitCompletionTimeout { get; init; }
    /// <summary>Gets the provider timeout resolution used to classify configured lifetimes.</summary>
    public required TimeSpan TimeoutGranularity { get; init; }
    /// <summary>Gets whether execution preserves request order.</summary>
    public bool Ordered { get; init; }
    /// <summary>Gets whether non-atomic modes report every executed item result.</summary>
    public bool PartialResults { get; init; }
    /// <summary>Gets whether one atomic execution may span collections on this store.</summary>
    public bool CrossCollectionAtomic { get; init; }
    /// <summary>Gets whether later items observe earlier provisional writes.</summary>
    public bool ReadYourWrites { get; init; }
    /// <summary>Gets whether committed records survive process restart.</summary>
    public bool Durable { get; init; }
    /// <summary>Gets whether mutation journal entries share the record transaction.</summary>
    public bool TransactionalJournal { get; init; }
    /// <summary>Gets the isolation classification independently of atomicity.</summary>
    public required BaseTransactionIsolation Isolation { get; init; }
    /// <summary>Gets whether nested transactions are supported. L30 providers return false.</summary>
    public bool NestedTransactions { get; init; }
    /// <summary>Gets whether savepoints are exposed. L30 providers return false.</summary>
    public bool Savepoints { get; init; }
}

/// <summary>Declares atomic record-ID upsert guarantees.</summary>
public sealed record StoreUpsertCapability
{
    /// <summary>Gets whether branch selection and write are atomic.</summary>
    public bool Atomic { get; init; }
    /// <summary>Gets the supported update-branch modes.</summary>
    public required RecordUpsertUpdateMode[] UpdateModes { get; init; }
    /// <summary>Gets whether the update branch supports expected revisions.</summary>
    public bool ExpectedRevision { get; init; }
    /// <summary>Gets whether create-only and update-only conditions are supported.</summary>
    public bool ExistenceConditions { get; init; }
}

/// <summary>Classifies transaction isolation separately from atomicity.</summary>
public enum BaseTransactionIsolation
{
    /// <summary>The provider has a bounded but provider-defined isolation model.</summary>
StoreDefined,
    /// <summary>Each statement observes committed state at statement start.</summary>
ReadCommitted,
    /// <summary>The transaction observes one repeatable committed snapshot.</summary>
RepeatableRead,
    /// <summary>Concurrent executions behave as a serial order.</summary>
Serializable
}

/// <summary>Represents a streaming capability.</summary>
public sealed record StreamingCapability
{
    /// <summary>Gets or sets the supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets the max items.</summary>
    public int? MaxItems { get; init; }
    /// <summary>Gets or sets the requires stable sort.</summary>
    public bool RequiresStableSort { get; init; }
}
