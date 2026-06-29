using System.Text.Json;
using HPD.Base.Query;
using HPD.Base.Results;
using HPD.Base.Stores;

namespace HPD.Base.Descriptors;

public sealed record CapabilityConstraintSet
{
    public StoreCrudCapabilityConstraints? StoreCrud { get; init; }
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
    public SearchCapabilityConstraints? Search { get; init; }
    public VectorCapabilityConstraints? Vector { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record StoreCrudCapabilityConstraints
{
    public string[]? Operations { get; init; }
    public IdAuthority IdAuthority { get; init; }
    public TimestampAuthority TimestampAuthority { get; init; }
    public ConsistencyModel Consistency { get; init; }
    public int? MaxPageSize { get; init; }
    public bool SupportsIdempotencyKey { get; init; }
}

public sealed record StoreRevisionCapabilityConstraints
{
    public bool Patch { get; init; }
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

public sealed record BatchCapabilityConstraints
{
    public bool Supported { get; init; }
    public int? MaxOperations { get; init; }
    public string[]? FeatureIds { get; init; }
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
