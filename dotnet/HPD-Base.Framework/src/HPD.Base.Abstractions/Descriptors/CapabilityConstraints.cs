using System.Text.Json;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Stores;

namespace HPD.Base.Descriptors;

public sealed record CapabilityConstraintSet
{
    /// <summary>Gets record-read constraints.</summary>
    public StoreReadCapabilityConstraints? StoreRead { get; init; }
    /// <summary>Gets record-mutation constraints.</summary>
    public StoreMutationCapabilityConstraints? StoreMutation { get; init; }
    public StoreRevisionCapabilityConstraints? StoreRevision { get; init; }
    public StoreStreamingCapabilityConstraints? StoreStreaming { get; init; }
    public QueryFilterCapabilityConstraints? QueryFilter { get; init; }
    public QuerySortCapabilityConstraints? QuerySort { get; init; }
    public QueryPaginationCapabilityConstraints? QueryPagination { get; init; }
    public QueryCountCapabilityConstraints? QueryCount { get; init; }
    public QuerySelectCapabilityConstraints? QuerySelect { get; init; }
    public QueryIncludeCapabilityConstraints? QueryInclude { get; init; }
    public PolicyEvaluationCapabilityConstraints? PolicyEvaluation { get; init; }
    public SchemaReadCapabilityConstraints? SchemaRead { get; init; }
    public EventStreamCapabilityConstraints? EventStream { get; init; }
    public ProjectionCapabilityConstraints? Projection { get; init; }
    public FileCapabilityConstraints? Files { get; init; }
    public RealtimeCapabilityConstraints? Realtime { get; init; }
    public BatchCapabilityConstraints? Batch { get; init; }
    /// <summary>Gets atomic record-ID upsert constraints.</summary>
    public UpsertCapabilityConstraints? Upsert { get; init; }
    public SearchCapabilityConstraints? Search { get; init; }
    public VectorCapabilityConstraints? Vector { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Describes record-read operations and their page bound.</summary>
public sealed record StoreReadCapabilityConstraints
{
    /// <summary>Gets the supported read operation names.</summary>
    public string[]? Operations { get; init; }
    /// <summary>Gets the maximum page size.</summary>
    public int? MaxPageSize { get; init; }
}

/// <summary>Describes physical mutation support and provider authority.</summary>
public sealed record StoreMutationCapabilityConstraints
{
    /// <summary>Gets the supported mutation operation names.</summary>
    public string[]? Operations { get; init; }
    /// <summary>Gets which layer supplies identifiers.</summary>
    public IdAuthority IdAuthority { get; init; }
    /// <summary>Gets which layer supplies persisted timestamps.</summary>
    public TimestampAuthority TimestampAuthority { get; init; }
    /// <summary>Gets the consistency classification.</summary>
    public ConsistencyModel Consistency { get; init; }
}

public sealed record StoreRevisionCapabilityConstraints
{
    /// <summary>Gets whether conditional patch is enforced.</summary>
    public bool Patch { get; init; }
    /// <summary>Gets whether conditional replace is enforced.</summary>
    public bool Replace { get; init; }
    /// <summary>Gets whether conditional delete is enforced.</summary>
    public bool Delete { get; init; }
    public RevisionGuarantee Guarantee { get; init; }
}

public sealed record StoreStreamingCapabilityConstraints
{
    public int? MaxItems { get; init; }
    public bool RequiresStableSort { get; init; }
}

public sealed record QueryFilterCapabilityConstraints
{
    public FilterOperator[]? Operators { get; init; }
    public bool BooleanComposition { get; init; }
    public bool Not { get; init; }
    public bool NullChecks { get; init; }
    public bool MissingFieldChecks { get; init; }
    public bool NestedFieldPaths { get; init; }
    public bool ArrayMembership { get; init; }
    public int? MaxDepth { get; init; }
    public int? MaxNodes { get; init; }
    public int? MaxSerializedLength { get; init; }
    public QueryExecutionMode ExecutionMode { get; init; }
}

public sealed record QuerySortCapabilityConstraints
{
    public int? MaxFields { get; init; }
    public bool NestedFieldPaths { get; init; }
    public bool NullOrdering { get; init; }
    public bool StableTieBreaker { get; init; }
    public string[]? DefaultSort { get; init; }
}

public sealed record QueryPaginationCapabilityConstraints
{
    public bool Page { get; init; }
    public bool Offset { get; init; }
    public bool Cursor { get; init; }
    public int DefaultLimit { get; init; }
    public int MaxLimit { get; init; }
    public bool CursorRequiresStableSort { get; init; }
}

public sealed record QueryCountCapabilityConstraints
{
    public QueryCountMode[]? SupportedModes { get; init; }
    public bool CountMayBeExpensive { get; init; }
}

public sealed record QuerySelectCapabilityConstraints
{
    public bool PayloadFields { get; init; }
    public bool SystemFields { get; init; }
    public bool NestedFieldPaths { get; init; }
}

public sealed record QueryIncludeCapabilityConstraints
{
    public int MaxDepth { get; init; }
    public bool BackRelations { get; init; }
    public bool IncludeFilters { get; init; }
    public bool IncludeSort { get; init; }
    public bool IncludeLimit { get; init; }
    public QueryExecutionMode ExecutionMode { get; init; }
}

public sealed record PolicyEvaluationCapabilityConstraints
{
    public bool Supported { get; init; }
    public string[]? FeatureIds { get; init; }
    public string[]? RouteRefs { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record SchemaReadCapabilityConstraints
{
    public bool Supported { get; init; }
    public bool IncludesDiagnostics { get; init; }
    public string[]? DtoRefs { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record EventStreamCapabilityConstraints
{
    public bool Publish { get; init; }
    public bool Sink { get; init; }
    public int? MaxEnvelopeBytes { get; init; }
    public string[]? EventTypeRefs { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record ProjectionCapabilityConstraints
{
    public bool Available { get; init; }
    public string[]? RouteRefs { get; init; }
    public string[]? DtoRefs { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record FileCapabilityConstraints
{
    public bool Read { get; init; }
    public bool Write { get; init; }
    public long? MaxBytes { get; init; }
    public string[]? FeatureIds { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RealtimeCapabilityConstraints
{
    public bool Subscribe { get; init; }
    public int? MaxSubscriptions { get; init; }
    public string[]? FeatureIds { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Describes ordered batch guarantees and bounds.</summary>
public sealed record BatchCapabilityConstraints
{
    /// <summary>Gets the supported execution modes.</summary>
    public required BaseRecordBatchExecutionMode[] Modes { get; init; }
    /// <summary>Gets the maximum operations per request.</summary>
    public required int MaxOperations { get; init; }
    /// <summary>Gets the maximum canonical serialized payload bytes.</summary>
    public required long MaxCanonicalPayloadBytes { get; init; }
    /// <summary>Gets whether execution preserves request order.</summary>
    public bool Ordered { get; init; }
    /// <summary>Gets whether independent execution returns partial results.</summary>
    public bool PartialResults { get; init; }
    /// <summary>Gets whether one atomic batch may span collections.</summary>
    public bool CrossCollectionAtomic { get; init; }
    /// <summary>Gets whether later items observe earlier provisional writes.</summary>
    public bool ReadYourWrites { get; init; }
    /// <summary>Gets whether committed records are durable.</summary>
    public bool Durable { get; init; }
    /// <summary>Gets whether journal entries share the record transaction.</summary>
    public bool TransactionalJournal { get; init; }
    /// <summary>Gets the isolation classification.</summary>
    public required BaseTransactionIsolation Isolation { get; init; }
    /// <summary>Gets whether nested transactions are supported.</summary>
    public bool NestedTransactions { get; init; }
    /// <summary>Gets whether savepoints are exposed.</summary>
    public bool Savepoints { get; init; }
    /// <summary>Gets namespaced optional extension metadata.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Describes atomic record-ID upsert guarantees.</summary>
public sealed record UpsertCapabilityConstraints
{
    /// <summary>Gets whether branch selection and mutation are atomic.</summary>
    public bool Atomic { get; init; }
    /// <summary>Gets the supported update-branch modes.</summary>
    public required RecordUpsertUpdateMode[] UpdateModes { get; init; }
    /// <summary>Gets whether expected revision is supported on update.</summary>
    public bool ExpectedRevision { get; init; }
    /// <summary>Gets whether create-only and update-only conditions are supported.</summary>
    public bool ExistenceConditions { get; init; }
    /// <summary>Gets namespaced optional extension metadata.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record SearchCapabilityConstraints
{
    public bool Supported { get; init; }
    public string[]? FeatureIds { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record VectorCapabilityConstraints
{
    public bool Supported { get; init; }
    public int? MaxDimensions { get; init; }
    public string[]? FeatureIds { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
