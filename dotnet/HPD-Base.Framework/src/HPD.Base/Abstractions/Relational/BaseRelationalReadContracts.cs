namespace HPD.Base;
/// <summary>Identifies a portable relational join operation.</summary>
public enum BaseJoinKind
{
    /// <summary>Identifies inner.</summary>
Inner,
    /// <summary>Identifies left.</summary>
Left,
    /// <summary>Identifies semi.</summary>
Semi,
    /// <summary>Identifies anti.</summary>
Anti
}

/// <summary>Identifies a portable relational aggregate.</summary>
public enum BaseAggregateKind
{
    /// <summary>Identifies count.</summary>
Count,
    /// <summary>Identifies count Distinct.</summary>
CountDistinct,
    /// <summary>Identifies sum.</summary>
Sum,
    /// <summary>Identifies average.</summary>
Average,
    /// <summary>Identifies minimum.</summary>
Minimum,
    /// <summary>Identifies maximum.</summary>
Maximum,
    /// <summary>Identifies any.</summary>
Any,
    /// <summary>Identifies all.</summary>
All
}

/// <summary>Identifies one closed relational operand branch.</summary>
public enum BaseRelationalOperandKind
{
    /// <summary>Identifies source Field.</summary>
SourceField,
    /// <summary>Identifies record Id.</summary>
RecordId,
    /// <summary>Identifies parameter.</summary>
Parameter,
    /// <summary>Identifies aggregate.</summary>
Aggregate,
    /// <summary>Identifies literal.</summary>
Literal
}

/// <summary>Defines consistency required by a relational read.</summary>
public enum BaseReadConsistency
{
    /// <summary>Identifies snapshot.</summary>
Snapshot
}

/// <summary>Defines dependency evidence required from read execution.</summary>
public enum BaseReadDependencyMode
{
    /// <summary>Identifies complete.</summary>
Complete
}

/// <summary>Defines one registered source in a relational read.</summary>
public sealed record BaseRelationalReadSource
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
}

/// <summary>Defines one closed typed relational operand.</summary>
public sealed record BaseRelationalOperand
{
    /// <summary>Gets or sets kind.</summary>
    public required BaseRelationalOperandKind Kind { get; init; }
    /// <summary>Gets or sets source Id.</summary>
    public string? SourceId { get; init; }
    /// <summary>Gets or sets field Id.</summary>
    public string? FieldId { get; init; }
    /// <summary>Gets or sets parameter Id.</summary>
    public string? ParameterId { get; init; }
    /// <summary>Gets or sets aggregate Id.</summary>
    public string? AggregateId { get; init; }
    /// <summary>Gets or sets literal.</summary>
    public QueryValue? Literal { get; init; }
}

/// <summary>Defines an equality join between two registered sources.</summary>
public sealed record BaseRelationalReadJoin
{
    /// <summary>Gets or sets kind.</summary>
    public required BaseJoinKind Kind { get; init; }
    /// <summary>Gets or sets left.</summary>
    public required BaseRelationalOperand Left { get; init; }
    /// <summary>Gets or sets right.</summary>
    public required BaseRelationalOperand Right { get; init; }
}

/// <summary>Defines one closed relational predicate node.</summary>
public sealed record BaseRelationalPredicate
{
    /// <summary>Gets or sets kind.</summary>
    public required FilterNodeKind Kind { get; init; }
    /// <summary>Gets or sets operator.</summary>
    public FilterOperator Operator { get; init; }
    /// <summary>Gets or sets left.</summary>
    public BaseRelationalOperand? Left { get; init; }
    /// <summary>Gets or sets right.</summary>
    public BaseRelationalOperand? Right { get; init; }
    /// <summary>Gets or sets children.</summary>
    public BaseRelationalPredicate[]? Children { get; init; }
}

/// <summary>Defines one registered aggregate output.</summary>
public sealed record BaseRelationalReadAggregate
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets kind.</summary>
    public required BaseAggregateKind Kind { get; init; }
    /// <summary>Gets or sets operand.</summary>
    public BaseRelationalOperand? Operand { get; init; }
}

/// <summary>Maps one projection field to a closed operand.</summary>
public sealed record BaseRelationalReadProjection
{
    /// <summary>Gets or sets field Id.</summary>
    public required string FieldId { get; init; }
    /// <summary>Gets or sets operand.</summary>
    public required BaseRelationalOperand Operand { get; init; }
}

/// <summary>Defines one deterministic relational sort.</summary>
public sealed record BaseRelationalReadSort
{
    /// <summary>Gets or sets operand.</summary>
    public required BaseRelationalOperand Operand { get; init; }
    /// <summary>Gets or sets direction.</summary>
    public QuerySortDirection Direction { get; init; }
    /// <summary>Gets or sets nulls.</summary>
    public QueryNullOrder Nulls { get; init; }
}

/// <summary>Defines immutable execution budgets for one registered read.</summary>
public sealed record BaseRelationalReadBudgets
{
    /// <summary>Gets or sets max Result Rows.</summary>
    public required int MaxResultRows { get; init; }
    /// <summary>Gets or sets max Result Bytes.</summary>
    public required int MaxResultBytes { get; init; }
    /// <summary>Gets or sets max Operations.</summary>
    public required int MaxOperations { get; init; }
}

/// <summary>Defines one closed typed parameter accepted by a registered read.</summary>
public sealed record BaseRelationalReadParameter
{
    /// <summary>Gets the stable parameter identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the canonical scalar kind, or <see cref = "QueryValueKind.Array"/> for an array parameter.</summary>
    public required QueryValueKind Kind { get; init; }
    /// <summary>Gets the canonical element kind for an array parameter.</summary>
    public QueryValueKind? ElementKind { get; init; }
    /// <summary>Gets whether the complete parameter may be null.</summary>
    public bool Nullable { get; init; }
    /// <summary>Gets the maximum string or identifier length, when applicable.</summary>
    public int? MaxLength { get; init; }
    /// <summary>Gets the maximum number of array elements, when applicable.</summary>
    public int? MaxItems { get; init; }
}

/// <summary>Defines the complete closed provider-neutral relational read plan.</summary>
public sealed record BaseRelationalReadPlan
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets schema Generation.</summary>
    public long SchemaGeneration { get; init; }
    /// <summary>Gets or sets sources.</summary>
    public required BaseRelationalReadSource[] Sources { get; init; }
    /// <summary>Gets or sets joins.</summary>
    public BaseRelationalReadJoin[] Joins { get; init; } = [];
    /// <summary>Gets or sets predicate.</summary>
    public BaseRelationalPredicate? Predicate { get; init; }
    /// <summary>Gets or sets group Keys.</summary>
    public BaseRelationalOperand[] GroupKeys { get; init; } = [];
    /// <summary>Gets or sets aggregates.</summary>
    public BaseRelationalReadAggregate[] Aggregates { get; init; } = [];
    /// <summary>Gets or sets having.</summary>
    public BaseRelationalPredicate? Having { get; init; }
    /// <summary>Gets or sets projection.</summary>
    public required BaseRelationalReadProjection[] Projection { get; init; }
    /// <summary>Gets or sets distinct.</summary>
    public bool Distinct { get; init; }
    /// <summary>Gets or sets sort.</summary>
    public BaseRelationalReadSort[] Sort { get; init; } = [];
    /// <summary>Gets or sets parameters.</summary>
    public required BaseRelationalReadParameter[] Parameters { get; init; }
    /// <summary>Gets or sets consistency.</summary>
    public BaseReadConsistency Consistency { get; init; } = BaseReadConsistency.Snapshot;
    /// <summary>Gets or sets dependency Mode.</summary>
    public BaseReadDependencyMode DependencyMode { get; init; } = BaseReadDependencyMode.Complete;
    /// <summary>Gets or sets budgets.</summary>
    public required BaseRelationalReadBudgets Budgets { get; init; }
    /// <summary>Gets or sets page.</summary>
    public BaseReadPageRequest? Page { get; init; }
}

/// <summary>Defines one canonical relational row value.</summary>
public sealed record BaseRelationalRow
{
    /// <summary>Gets or sets fields.</summary>
    public required BaseRelationalFieldValue[] Fields { get; init; }
}

/// <summary>Associates one stable projection field with its closed value.</summary>
public sealed record BaseRelationalFieldValue
{
    /// <summary>Gets or sets field Id.</summary>
    public required string FieldId { get; init; }
    /// <summary>Gets or sets value.</summary>
    public required QueryValue Value { get; init; }
}

/// <summary>Returns a completely buffered and validated provider result.</summary>
public sealed record BaseRelationalReadResult
{
    /// <summary>Gets or sets rows.</summary>
    public required BaseRelationalRow[] Rows { get; init; }
    /// <summary>Gets or sets page.</summary>
    public required PageInfo Page { get; init; }
    /// <summary>Gets or sets count.</summary>
    public long? Count { get; init; }
    /// <summary>Gets or sets schema Generation.</summary>
    public long SchemaGeneration { get; init; }
}

/// <summary>Binds one closed request value to a stable parameter identifier.</summary>
public sealed record BaseRelationalParameterValue
{
    /// <summary>Gets or sets parameter Id.</summary>
    public required string ParameterId { get; init; }
    /// <summary>Gets or sets value.</summary>
    public required QueryValue Value { get; init; }
}

/// <summary>Provides one source's current independently evaluated read policy.</summary>
public sealed record BaseRelationalReadSourcePolicy
{
    /// <summary>Gets the definition-local source identifier.</summary>
    public required string SourceId { get; init; }
    /// <summary>Gets the stable source collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the current record-membership constraint.</summary>
    public FilterExpression? Filter { get; init; }
    /// <summary>Gets the current field-visibility constraint.</summary>
    public FieldMask? ReadMask { get; init; }
}

/// <summary>Defines one bounded provider execution request.</summary>
public sealed record BaseRelationalReadExecutionRequest
{
    /// <summary>Gets or sets plan.</summary>
    public required BaseRelationalReadPlan Plan { get; init; }
    /// <summary>Gets or sets parameter Values.</summary>
    public required BaseRelationalParameterValue[] ParameterValues { get; init; }
    /// <summary>Gets or sets source Policies.</summary>
    public required BaseRelationalReadSourcePolicy[] SourcePolicies { get; init; }
    /// <summary>Gets or sets operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets acquisition Timeout.</summary>
    public TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets or sets execution Timeout.</summary>
    public TimeSpan ExecutionTimeout { get; init; }
    /// <summary>Gets or sets max Result Rows.</summary>
    public int MaxResultRows { get; init; }
    /// <summary>Gets or sets max Result Bytes.</summary>
    public int MaxResultBytes { get; init; }
}

/// <summary>Returns complete rows and dependency evidence from a provider.</summary>
public sealed record BaseRelationalReadExecutionResult
{
    /// <summary>Gets or sets result.</summary>
    public required BaseRelationalReadResult Result { get; init; }
    /// <summary>Gets or sets dependency Evidence.</summary>
    public required BaseReadDependencyEvidence[] DependencyEvidence { get; init; }
}

/// <summary>Describes trusted same-snapshot evidence before Runtime protection.</summary>
public sealed record BaseReadDependencyEvidence
{
    /// <summary>Gets the contributing collection identity.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets an optional contributing record identity.</summary>
    public string? RecordId { get; init; }
}

/// <summary>Describes callable relational-read provider support.</summary>
public sealed record RelationalReadCapability
{
    /// <summary>Gets or sets supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets join Kinds.</summary>
    public required BaseJoinKind[] JoinKinds { get; init; }
    /// <summary>Gets or sets aggregate Kinds.</summary>
    public required BaseAggregateKind[] AggregateKinds { get; init; }
    /// <summary>Gets or sets comparison Operators.</summary>
    public required FilterOperator[] ComparisonOperators { get; init; }
    /// <summary>Gets or sets value Kinds.</summary>
    public required QueryValueKind[] ValueKinds { get; init; }
    /// <summary>Gets or sets max Sources.</summary>
    public int MaxSources { get; init; }
    /// <summary>Gets or sets max Joins.</summary>
    public int MaxJoins { get; init; }
    /// <summary>Gets or sets max Predicate Nodes.</summary>
    public int MaxPredicateNodes { get; init; }
    /// <summary>Gets or sets max Group Keys.</summary>
    public int MaxGroupKeys { get; init; }
    /// <summary>Gets or sets max Aggregates.</summary>
    public int MaxAggregates { get; init; }
    /// <summary>Gets or sets max Projection Fields.</summary>
    public int MaxProjectionFields { get; init; }
    /// <summary>Gets or sets max Sort Fields.</summary>
    public int MaxSortFields { get; init; }
    /// <summary>Gets or sets max Result Rows.</summary>
    public int MaxResultRows { get; init; }
    /// <summary>Gets or sets max Result Bytes.</summary>
    public int MaxResultBytes { get; init; }
    /// <summary>Gets or sets snapshot Consistency.</summary>
    public bool SnapshotConsistency { get; init; }
    /// <summary>Gets or sets complete Dependency Evidence.</summary>
    public bool CompleteDependencyEvidence { get; init; }
}

/// <summary>Executes complete registered relational reads.</summary>
public interface IRelationalReadStore : IRecordStore
{
    /// <summary>Gets relational Reads.</summary>
    RelationalReadCapability RelationalReads { get; }

    /// <summary>Performs execute Read Async.</summary>
    ValueTask<OperationResult<BaseRelationalReadExecutionResult>> ExecuteReadAsync(BaseRelationalReadExecutionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Describes callable snapshot-consistent include support.</summary>
public sealed record RecordIncludeExecutionCapability
{
    /// <summary>Gets or sets supported.</summary>
    public bool Supported { get; init; }
    /// <summary>Gets or sets max Depth.</summary>
    public int MaxDepth { get; init; }
    /// <summary>Gets or sets max Includes.</summary>
    public int MaxIncludes { get; init; }
    /// <summary>Gets or sets max Records.</summary>
    public int MaxRecords { get; init; }
    /// <summary>Gets or sets snapshot Consistency.</summary>
    public bool SnapshotConsistency { get; init; }
}

/// <summary>Provides the already-composed policy for one include source.</summary>
public sealed record RecordIncludeSourcePolicy
{
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets filter.</summary>
    public FilterExpression? Filter { get; init; }
    /// <summary>Gets or sets read Mask.</summary>
    public FieldMask? ReadMask { get; init; }
}

/// <summary>Defines one bounded snapshot-consistent include request.</summary>
public sealed record RecordIncludeExecutionRequest
{
    /// <summary>Gets or sets root Collection.</summary>
    public required CollectionDefinition RootCollection { get; init; }
    /// <summary>Gets or sets root Query.</summary>
    public required RecordQuery RootQuery { get; init; }
    /// <summary>Gets or sets include Plan.</summary>
    public required RecordInclude[] IncludePlan { get; init; }
    /// <summary>Gets or sets source Policies.</summary>
    public required RecordIncludeSourcePolicy[] SourcePolicies { get; init; }
    /// <summary>Gets or sets operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets acquisition Timeout.</summary>
    public TimeSpan AcquisitionTimeout { get; init; }
    /// <summary>Gets or sets execution Timeout.</summary>
    public TimeSpan ExecutionTimeout { get; init; }
    /// <summary>Gets or sets max Result Rows.</summary>
    public int MaxResultRows { get; init; }
    /// <summary>Gets or sets max Result Bytes.</summary>
    public int MaxResultBytes { get; init; }
}

/// <summary>Returns one complete root page with structural includes.</summary>
public sealed record RecordIncludeExecutionResult
{
    /// <summary>Gets or sets page.</summary>
    public required RecordPage Page { get; init; }
    /// <summary>Gets or sets schema Generation.</summary>
    public long SchemaGeneration { get; init; }
    /// <summary>Gets or sets dependency Evidence.</summary>
    public required BaseReadDependencyEvidence[] DependencyEvidence { get; init; }
}

/// <summary>Executes structural includes under one provider snapshot.</summary>
public interface IConsistentRecordIncludeStore : IRecordStore
{
    /// <summary>Gets includes.</summary>
    RecordIncludeExecutionCapability Includes { get; }

    /// <summary>Performs execute Include Async.</summary>
    ValueTask<OperationResult<RecordIncludeExecutionResult>> ExecuteIncludeAsync(RecordIncludeExecutionRequest request, CancellationToken cancellationToken = default);
}
