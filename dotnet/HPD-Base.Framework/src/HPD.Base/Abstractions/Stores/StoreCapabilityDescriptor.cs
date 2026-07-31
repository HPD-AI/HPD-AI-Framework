using System.Text.Json;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;

namespace HPD.Base.Stores;

public sealed record StoreCapabilityDescriptor
{
    public required string StoreId { get; init; }
    public required string StoreKind { get; init; }
    public required string StoreVersion { get; init; }
    /// <summary>Gets portable record-read support and limits.</summary>
    public required RecordReadCapability Read { get; init; }
    /// <summary>Gets portable record-mutation support and authority.</summary>
    public required RecordMutationCapability Mutation { get; init; }
    public required QueryCapability Query { get; init; }
    public RevisionCapability? Revision { get; init; }
    /// <summary>Gets ordered and atomic batch guarantees, when implemented.</summary>
    public StoreBatchCapability? Batch { get; init; }
    /// <summary>Gets atomic record-ID upsert guarantees, when implemented.</summary>
    public StoreUpsertCapability? Upsert { get; init; }
    public StreamingCapability? Streaming { get; init; }
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
}

public enum IdAuthority { Runtime, Store, Client, Hybrid }
public enum TimestampAuthority { Runtime, Store, Client, Hybrid, None }
public enum ConsistencyModel { Strong, Eventual, Session, StoreDefined }

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

public sealed record StreamingCapability
{
    public bool Supported { get; init; }
    public int? MaxItems { get; init; }
    public bool RequiresStableSort { get; init; }
}
