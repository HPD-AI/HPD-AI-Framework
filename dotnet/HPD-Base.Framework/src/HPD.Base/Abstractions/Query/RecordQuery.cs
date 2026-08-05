namespace HPD.Base;
/// <summary>Represents record Query.</summary>
public sealed record RecordQuery
{
    /// <summary>Gets or sets filter.</summary>
    public FilterExpression? Filter { get; init; }
    /// <summary>Gets or sets sort.</summary>
    public QuerySort[]? Sort { get; init; }
    /// <summary>Gets or sets page.</summary>
    public QueryPage? Page { get; init; }
    /// <summary>Gets or sets select.</summary>
    public string[]? Select { get; init; }
    /// <summary>Gets or sets include.</summary>
    public RecordInclude[]? Include { get; init; }
    /// <summary>Gets or sets count.</summary>
    public QueryCountMode Count { get; init; } = QueryCountMode.IfAvailable;
    /// <summary>Gets or sets extensions.</summary>
    public QueryExtension[]? Extensions { get; init; }
}

/// <summary>Represents query Sort.</summary>
public readonly record struct QuerySort(string Field, QuerySortDirection Direction = QuerySortDirection.Asc, QueryNullOrder Nulls = QueryNullOrder.Unspecified);
/// <summary>Represents query Page.</summary>
public sealed record QueryPage
{
    /// <summary>Gets or sets mode.</summary>
    public QueryPaginationMode Mode { get; init; } = QueryPaginationMode.Page;
    /// <summary>Gets or sets page.</summary>
    public int? Page { get; init; }
    /// <summary>Gets or sets per Page.</summary>
    public int? PerPage { get; init; }
    /// <summary>Gets or sets offset.</summary>
    public int? Offset { get; init; }
    /// <summary>Gets or sets limit.</summary>
    public int? Limit { get; init; }
    /// <summary>Gets or sets cursor.</summary>
    public string? Cursor { get; init; }
    /// <summary>Gets or sets cursor Direction.</summary>
    public QueryCursorDirection CursorDirection { get; init; } = QueryCursorDirection.After;
}

/// <summary>Represents record Include.</summary>
public sealed record RecordInclude
{
    /// <summary>Gets or sets navigation Id.</summary>
    public required string NavigationId { get; init; }
    /// <summary>Gets or sets select Field Ids.</summary>
    public string[]? SelectFieldIds { get; init; }
    /// <summary>Gets or sets filter.</summary>
    public FilterExpression? Filter { get; init; }
    /// <summary>Gets or sets sort.</summary>
    public QuerySort[]? Sort { get; init; }
    /// <summary>Gets or sets limit.</summary>
    public int? Limit { get; init; }
    /// <summary>Gets or sets includes.</summary>
    public RecordInclude[]? Includes { get; init; }
}

/// <summary>Represents query Extension.</summary>
public sealed record QueryExtension
{
    /// <summary>Gets or sets module Id.</summary>
    public required string ModuleId { get; init; }
    /// <summary>Gets or sets name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets arguments.</summary>
    public QueryValue[]? Arguments { get; init; }
}
