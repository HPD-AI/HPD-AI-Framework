namespace HPD.Base;

/// <summary>
/// One-record discriminator shape for portable filter ASTs.
/// </summary>
public sealed record FilterExpression
{
    /// <summary>Gets or sets the kind.</summary>
    public required FilterNodeKind Kind { get; init; }
    /// <summary>Gets or sets the field.</summary>
    public string? Field { get; init; }
    /// <summary>Gets or sets the operator.</summary>
    public FilterOperator Operator { get; init; }
    /// <summary>Gets or sets the value.</summary>
    public QueryValue? Value { get; init; }
    /// <summary>Gets or sets the values.</summary>
    public QueryValue[]? Values { get; init; }
    /// <summary>Gets or sets the children.</summary>
    public FilterExpression[]? Children { get; init; }
    /// <summary>Gets or sets the module ID.</summary>
    public string? ModuleId { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public string? Name { get; init; }
    /// <summary>Gets or sets the arguments.</summary>
    public QueryValue[]? Arguments { get; init; }
}
