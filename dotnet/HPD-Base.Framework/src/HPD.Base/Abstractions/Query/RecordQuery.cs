namespace HPD.Base;

public sealed record RecordQuery
{
    public FilterExpression? Filter { get; init; }
    public QuerySort[]? Sort { get; init; }
    public QueryPage? Page { get; init; }
    public string[]? Select { get; init; }
    public QueryInclude[]? Include { get; init; }
    public QueryCountMode Count { get; init; } = QueryCountMode.IfAvailable;
    public QueryExtension[]? Extensions { get; init; }
}

public readonly record struct QuerySort(
    string Field,
    QuerySortDirection Direction = QuerySortDirection.Asc,
    QueryNullOrder Nulls = QueryNullOrder.Unspecified);

public sealed record QueryPage
{
    public QueryPaginationMode Mode { get; init; } = QueryPaginationMode.Page;
    public int? Page { get; init; }
    public int? PerPage { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }
    public string? Cursor { get; init; }
    public QueryCursorDirection CursorDirection { get; init; } = QueryCursorDirection.After;
}

public sealed record QueryInclude
{
    public required string Path { get; init; }
    public string[]? Select { get; init; }
    public FilterExpression? Filter { get; init; }
    public QuerySort[]? Sort { get; init; }
    public int? Limit { get; init; }
}

public sealed record QueryExtension
{
    public required string ModuleId { get; init; }
    public required string Name { get; init; }
    public QueryValue[]? Arguments { get; init; }
}
