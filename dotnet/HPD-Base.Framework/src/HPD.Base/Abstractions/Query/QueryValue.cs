namespace HPD.Base;

/// <summary>
/// Source-generation-friendly tagged value used by portable query contracts.
/// </summary>
public sealed record QueryValue
{
    /// <summary>Gets or sets the kind.</summary>
    public required QueryValueKind Kind { get; init; }
    /// <summary>Gets or sets the string.</summary>
    public string? String { get; init; }
    /// <summary>Gets or sets the boolean.</summary>
    public bool? Boolean { get; init; }
    /// <summary>Gets or sets the integer.</summary>
    public long? Integer { get; init; }
    /// <summary>Gets or sets the number.</summary>
    public double? Number { get; init; }
    /// <summary>Gets or sets the decimal.</summary>
    public string? Decimal { get; init; }
    /// <summary>Gets or sets the date time.</summary>
    public DateTimeOffset? DateTime { get; init; }
    /// <summary>Gets or sets the ID.</summary>
    public string? Id { get; init; }
    /// <summary>Gets or sets the array.</summary>
    public QueryValue[]? Array { get; init; }
}
