namespace HPD.Base;

/// <summary>
/// Source-generation-friendly tagged value used by portable query contracts.
/// </summary>
public sealed record QueryValue
{
    public required QueryValueKind Kind { get; init; }
    public string? String { get; init; }
    public bool? Boolean { get; init; }
    public long? Integer { get; init; }
    public double? Number { get; init; }
    public string? Decimal { get; init; }
    public DateTimeOffset? DateTime { get; init; }
    public string? Id { get; init; }
    public QueryValue[]? Array { get; init; }
}
