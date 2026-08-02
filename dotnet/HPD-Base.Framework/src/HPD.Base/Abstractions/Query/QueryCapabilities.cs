namespace HPD.Base;

/// <summary>Represents a query operator descriptor.</summary>
public sealed record QueryOperatorDescriptor
{
    /// <summary>Gets or sets the module ID.</summary>
    public required string ModuleId { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the placement.</summary>
    public required QueryOperatorPlacement Placement { get; init; }
    /// <summary>Gets or sets the argument kinds.</summary>
    public QueryValueKind[]? ArgumentKinds { get; init; }
    /// <summary>Gets or sets the allowed field types.</summary>
    public string[]? AllowedFieldTypes { get; init; }
    /// <summary>Gets or sets the usage profiles.</summary>
    public FilterUsage[]? UsageProfiles { get; init; }
    /// <summary>Gets or sets the field required.</summary>
    public bool FieldRequired { get; init; }
    /// <summary>Gets or sets the requires index.</summary>
    public bool RequiresIndex { get; init; }
    /// <summary>Gets or sets the capability path.</summary>
    public string? CapabilityPath { get; init; }
}

/// <summary>Represents a query capability.</summary>
public sealed record QueryCapability
{
    /// <summary>Gets or sets the filter.</summary>
    public required FilterCapability Filter { get; init; }
    /// <summary>Gets or sets the sort.</summary>
    public required SortCapability Sort { get; init; }
    /// <summary>Gets or sets the pagination.</summary>
    public required PaginationCapability Pagination { get; init; }
    /// <summary>Gets or sets the count.</summary>
    public required CountCapability Count { get; init; }
    /// <summary>Gets or sets the select.</summary>
    public required SelectCapability Select { get; init; }
    /// <summary>Gets or sets the include.</summary>
    public QueryIncludeCapability? Include { get; init; }
    /// <summary>Gets or sets the operators.</summary>
    public QueryOperatorDescriptor[]? Operators { get; init; }
}

/// <summary>Represents a filter capability.</summary>
public sealed record FilterCapability
{
    /// <summary>Gets or sets the supported.</summary>
    public bool Supported { get; init; }
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

/// <summary>Represents a sort capability.</summary>
public sealed record SortCapability
{
    /// <summary>Gets or sets the supported.</summary>
    public bool Supported { get; init; }
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

/// <summary>Represents a pagination capability.</summary>
public sealed record PaginationCapability
{
    /// <summary>Gets or sets the page.</summary>
    public bool Page { get; init; }
    /// <summary>Gets or sets the offset.</summary>
    public bool Offset { get; init; }
    /// <summary>Gets or sets the cursor.</summary>
    public bool Cursor { get; init; }
    /// <summary>Gets or sets the default limit.</summary>
    public int DefaultLimit { get; init; }
    /// <summary>Gets or sets the max limit.</summary>
    public int MaxLimit { get; init; }
    /// <summary>Gets or sets the cursor requires stable sort.</summary>
    public bool CursorRequiresStableSort { get; init; }
}

/// <summary>Represents a count capability.</summary>
public sealed record CountCapability
{
    /// <summary>Gets or sets the supported modes.</summary>
    public QueryCountMode[]? SupportedModes { get; init; }
    /// <summary>Gets or sets the count may be expensive.</summary>
    public bool CountMayBeExpensive { get; init; }
}

/// <summary>Represents a select capability.</summary>
public sealed record SelectCapability
{
    /// <summary>Gets or sets the payload fields.</summary>
    public bool PayloadFields { get; init; }
    /// <summary>Gets or sets the system fields.</summary>
    public bool SystemFields { get; init; }
    /// <summary>Gets or sets the nested field paths.</summary>
    public bool NestedFieldPaths { get; init; }
}

/// <summary>Represents a query include capability.</summary>
public sealed record QueryIncludeCapability
{
    /// <summary>Gets or sets the supported.</summary>
    public bool Supported { get; init; }
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
