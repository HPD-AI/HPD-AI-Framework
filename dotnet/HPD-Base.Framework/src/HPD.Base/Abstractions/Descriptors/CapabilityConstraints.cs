using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a capability constraint set.</summary>
public sealed record CapabilityConstraintSet
{
    /// <summary>Gets record-read constraints.</summary>
    public StoreReadCapabilityConstraints? StoreRead { get; init; }
    /// <summary>Gets record-mutation constraints.</summary>
    public StoreMutationCapabilityConstraints? StoreMutation { get; init; }
    /// <summary>Gets or sets the store revision.</summary>
    public StoreRevisionCapabilityConstraints? StoreRevision { get; init; }
    /// <summary>Gets or sets the store streaming.</summary>
    public StoreStreamingCapabilityConstraints? StoreStreaming { get; init; }
    /// <summary>Gets or sets the query filter.</summary>
    public QueryFilterCapabilityConstraints? QueryFilter { get; init; }
    /// <summary>Gets or sets the query sort.</summary>
    public QuerySortCapabilityConstraints? QuerySort { get; init; }
    /// <summary>Gets or sets the query pagination.</summary>
    public QueryPaginationCapabilityConstraints? QueryPagination { get; init; }
    /// <summary>Gets or sets the query count.</summary>
    public QueryCountCapabilityConstraints? QueryCount { get; init; }
    /// <summary>Gets or sets the query select.</summary>
    public QuerySelectCapabilityConstraints? QuerySelect { get; init; }
    /// <summary>Gets or sets the query include.</summary>
    public QueryIncludeCapabilityConstraints? QueryInclude { get; init; }
    /// <summary>Gets or sets the policy evaluation.</summary>
    public PolicyEvaluationCapabilityConstraints? PolicyEvaluation { get; init; }
    /// <summary>Gets or sets the schema read.</summary>
    public SchemaReadCapabilityConstraints? SchemaRead { get; init; }
    /// <summary>Gets or sets the event stream.</summary>
    public EventStreamCapabilityConstraints? EventStream { get; init; }
    /// <summary>Gets or sets the projection.</summary>
    public ProjectionCapabilityConstraints? Projection { get; init; }
    /// <summary>Gets or sets the files.</summary>
    public FileCapabilityConstraints? Files { get; init; }
    /// <summary>Gets or sets the realtime.</summary>
    public RealtimeCapabilityConstraints? Realtime { get; init; }
    /// <summary>Gets or sets the batch.</summary>
    public BatchCapabilityConstraints? Batch { get; init; }
    /// <summary>Gets atomic record-ID upsert constraints.</summary>
    public UpsertCapabilityConstraints? Upsert { get; init; }
    /// <summary>Gets or sets the search.</summary>
    public SearchCapabilityConstraints? Search { get; init; }
    /// <summary>Gets or sets the vector.</summary>
    public VectorCapabilityConstraints? Vector { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
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

/// <summary>Represents a store revision capability constraints.</summary>
public sealed record StoreRevisionCapabilityConstraints
{
    /// <summary>Gets whether conditional patch is enforced.</summary>
    public bool Patch { get; init; }
    /// <summary>Gets whether conditional replace is enforced.</summary>
    public bool Replace { get; init; }
    /// <summary>Gets whether conditional delete is enforced.</summary>
    public bool Delete { get; init; }
    /// <summary>Gets or sets the guarantee.</summary>
    public RevisionGuarantee Guarantee { get; init; }
}

/// <summary>Represents a store streaming capability constraints.</summary>
public sealed record StoreStreamingCapabilityConstraints
{
    /// <summary>Gets or sets the max items.</summary>
    public int? MaxItems { get; init; }
    /// <summary>Gets or sets the requires stable sort.</summary>
    public bool RequiresStableSort { get; init; }
}

/// <summary>Represents a query filter capability constraints.</summary>
public sealed record QueryFilterCapabilityConstraints
{
    /// <summary>Gets or sets the operators.</summary>
    public FilterOperator[]? Operators { get; init; }
    /// <summary>Gets or sets the boolean composition.</summary>
    public bool BooleanComposition { get; init; }
    /// <summary>Gets or sets the not.</summary>
    public bool Not { get; init; }
    /// <summary>Gets or sets the null checks.</summary>
    public bool NullChecks { get; init; }
    /// <summary>Gets or sets the missing field checks.</summary>
    public bool MissingFieldChecks { get; init; }
    /// <summary>Gets or sets the nested field paths.</summary>
    public bool NestedFieldPaths { get; init; }
    /// <summary>Gets or sets the array membership.</summary>
    public bool ArrayMembership { get; init; }
    /// <summary>Gets or sets the max depth.</summary>
    public int? MaxDepth { get; init; }
    /// <summary>Gets or sets the max nodes.</summary>
    public int? MaxNodes { get; init; }
    /// <summary>Gets or sets the max serialized length.</summary>
    public int? MaxSerializedLength { get; init; }
    /// <summary>Gets or sets the execution mode.</summary>
    public QueryExecutionMode ExecutionMode { get; init; }
}

/// <summary>Represents a query sort capability constraints.</summary>
public sealed record QuerySortCapabilityConstraints
{
    /// <summary>Gets or sets the max fields.</summary>
    public int? MaxFields { get; init; }
    /// <summary>Gets or sets the nested field paths.</summary>
    public bool NestedFieldPaths { get; init; }
    /// <summary>Gets or sets the null ordering.</summary>
    public bool NullOrdering { get; init; }
    /// <summary>Gets or sets the stable tie breaker.</summary>
    public bool StableTieBreaker { get; init; }
    /// <summary>Gets or sets the default sort.</summary>
    public string[]? DefaultSort { get; init; }
}

/// <summary>Represents a query pagination capability constraints.</summary>
public sealed record QueryPaginationCapabilityConstraints
{
    /// <summary>Gets or sets the page.</summary>
    public bool Page { get; init; }
    /// <summary>Gets or sets the offset.</summary>
    public bool Offset { get; init; }
    /// <summary>Gets or sets the strongest cursor guarantee.</summary>
    public QueryCursorGuarantee Cursor { get; init; }
    /// <summary>Gets or sets the default limit.</summary>
    public int DefaultLimit { get; init; }
    /// <summary>Gets or sets the max limit.</summary>
    public int MaxLimit { get; init; }
    /// <summary>Gets or sets the cursor requires stable sort.</summary>
    public bool CursorRequiresStableSort { get; init; }
}

/// <summary>Represents a query count capability constraints.</summary>
public sealed record QueryCountCapabilityConstraints
{
    /// <summary>Gets or sets the supported modes.</summary>
    public QueryCountMode[]? SupportedModes { get; init; }
    /// <summary>Gets or sets the count may be expensive.</summary>
    public bool CountMayBeExpensive { get; init; }
}

/// <summary>Represents a query select capability constraints.</summary>
public sealed record QuerySelectCapabilityConstraints
{
    /// <summary>Gets or sets the payload fields.</summary>
    public bool PayloadFields { get; init; }
    /// <summary>Gets or sets the system fields.</summary>
    public bool SystemFields { get; init; }
    /// <summary>Gets or sets the nested field paths.</summary>
    public bool NestedFieldPaths { get; init; }
}

/// <summary>Represents a query include capability constraints.</summary>
public sealed record QueryIncludeCapabilityConstraints
{
    /// <summary>Gets or sets the max depth.</summary>
    public int MaxDepth { get; init; }
    /// <summary>Gets or sets the back relations.</summary>
    public bool BackRelations { get; init; }
    /// <summary>Gets or sets the include filters.</summary>
    public bool IncludeFilters { get; init; }
    /// <summary>Gets or sets the include sort.</summary>
    public bool IncludeSort { get; init; }
    /// <summary>Gets or sets the include limit.</summary>
    public bool IncludeLimit { get; init; }
    /// <summary>Gets or sets the execution mode.</summary>
    public QueryExecutionMode ExecutionMode { get; init; }
}

/// <summary>Represents a policy evaluation capability constraints.</summary>
public sealed record PolicyEvaluationCapabilityConstraints
{
    /// <summary>Gets or sets the supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets the feature IDs.</summary>
    public string[]? FeatureIds { get; init; }
    /// <summary>Gets or sets the route refs.</summary>
    public string[]? RouteRefs { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a schema read capability constraints.</summary>
public sealed record SchemaReadCapabilityConstraints
{
    /// <summary>Gets or sets the supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets the includes diagnostics.</summary>
    public bool IncludesDiagnostics { get; init; }
    /// <summary>Gets or sets the DTO refs.</summary>
    public string[]? DtoRefs { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a event stream capability constraints.</summary>
public sealed record EventStreamCapabilityConstraints
{
    /// <summary>Gets or sets the publish.</summary>
    public bool Publish { get; init; }
    /// <summary>Gets or sets the sink.</summary>
    public bool Sink { get; init; }
    /// <summary>Gets or sets the max envelope bytes.</summary>
    public int? MaxEnvelopeBytes { get; init; }
    /// <summary>Gets or sets the event type refs.</summary>
    public string[]? EventTypeRefs { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a projection capability constraints.</summary>
public sealed record ProjectionCapabilityConstraints
{
    /// <summary>Gets or sets the available.</summary>
    public bool Available { get; init; }
    /// <summary>Gets or sets the route refs.</summary>
    public string[]? RouteRefs { get; init; }
    /// <summary>Gets or sets the DTO refs.</summary>
    public string[]? DtoRefs { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a file capability constraints.</summary>
public sealed record FileCapabilityConstraints
{
    /// <summary>Gets or sets the read.</summary>
    public bool Read { get; init; }
    /// <summary>Gets or sets the write.</summary>
    public bool Write { get; init; }
    /// <summary>Gets or sets the max bytes.</summary>
    public long? MaxBytes { get; init; }
    /// <summary>Gets or sets the feature IDs.</summary>
    public string[]? FeatureIds { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a realtime capability constraints.</summary>
public sealed record RealtimeCapabilityConstraints
{
    /// <summary>Gets or sets the subscribe.</summary>
    public bool Subscribe { get; init; }
    /// <summary>Gets or sets the max subscriptions.</summary>
    public int? MaxSubscriptions { get; init; }
    /// <summary>Gets or sets the feature IDs.</summary>
    public string[]? FeatureIds { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
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

/// <summary>Represents a search capability constraints.</summary>
public sealed record SearchCapabilityConstraints
{
    /// <summary>Gets or sets the supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets the feature IDs.</summary>
    public string[]? FeatureIds { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a vector capability constraints.</summary>
public sealed record VectorCapabilityConstraints
{
    /// <summary>Gets or sets the supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets the max dimensions.</summary>
    public int? MaxDimensions { get; init; }
    /// <summary>Gets or sets the feature IDs.</summary>
    public string[]? FeatureIds { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
