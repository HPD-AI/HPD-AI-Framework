namespace HPD.Base;

/// <summary>
/// Customizes one property in a generated BASE collection contract.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BaseFieldAttribute(string id) : Attribute
{
    /// <summary>
    /// Gets the stable logical field identifier.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets or sets the canonical stored field name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets whether the field is omitted from the generated contract.
    /// </summary>
    public bool Ignore { get; set; }

    /// <summary>
    /// Gets or sets the supported query operations.
    /// </summary>
    public BaseFieldOperator Operators { get; set; } = BaseFieldOperator.Equal;
}
