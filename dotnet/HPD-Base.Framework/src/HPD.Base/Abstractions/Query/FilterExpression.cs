namespace HPD.Base.Query;

/// <summary>
/// One-record discriminator shape for portable filter ASTs.
/// </summary>
public sealed record FilterExpression
{
    public required FilterNodeKind Kind { get; init; }
    public string? Field { get; init; }
    public FilterOperator Operator { get; init; }
    public QueryValue? Value { get; init; }
    public QueryValue[]? Values { get; init; }
    public FilterExpression[]? Children { get; init; }
    public string? ModuleId { get; init; }
    public string? Name { get; init; }
    public QueryValue[]? Arguments { get; init; }
}
