namespace HPD.Base.Query;

public sealed record QueryOperatorDescriptor
{
    public required string ModuleId { get; init; }
    public required string Name { get; init; }
    public required QueryOperatorPlacement Placement { get; init; }
    public QueryValueKind[]? ArgumentKinds { get; init; }
    public string[]? AllowedFieldTypes { get; init; }
    public FilterUsage[]? UsageProfiles { get; init; }
    public bool FieldRequired { get; init; }
    public bool RequiresIndex { get; init; }
    public string? CapabilityPath { get; init; }
}

public sealed record QueryCapability
{
    public required FilterCapability Filter { get; init; }
    public required SortCapability Sort { get; init; }
    public required PaginationCapability Pagination { get; init; }
    public required CountCapability Count { get; init; }
    public required SelectCapability Select { get; init; }
    public QueryIncludeCapability? Include { get; init; }
    public QueryOperatorDescriptor[]? Operators { get; init; }
}

public sealed record FilterCapability
{
    public bool Supported { get; init; }
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

public sealed record SortCapability
{
    public bool Supported { get; init; }
    public int? MaxFields { get; init; }
    public bool NestedFieldPaths { get; init; }
    public bool NullOrdering { get; init; }
    public bool StableTieBreaker { get; init; }
    public string[]? DefaultSort { get; init; }
}

public sealed record PaginationCapability
{
    public bool Page { get; init; }
    public bool Offset { get; init; }
    public bool Cursor { get; init; }
    public int DefaultLimit { get; init; }
    public int MaxLimit { get; init; }
    public bool CursorRequiresStableSort { get; init; }
}

public sealed record CountCapability
{
    public QueryCountMode[]? SupportedModes { get; init; }
    public bool CountMayBeExpensive { get; init; }
}

public sealed record SelectCapability
{
    public bool PayloadFields { get; init; }
    public bool SystemFields { get; init; }
    public bool NestedFieldPaths { get; init; }
}

public sealed record QueryIncludeCapability
{
    public bool Supported { get; init; }
    public int MaxDepth { get; init; }
    public bool BackRelations { get; init; }
    public bool IncludeFilters { get; init; }
    public bool IncludeSort { get; init; }
    public bool IncludeLimit { get; init; }
    public QueryExecutionMode ExecutionMode { get; init; }
}
