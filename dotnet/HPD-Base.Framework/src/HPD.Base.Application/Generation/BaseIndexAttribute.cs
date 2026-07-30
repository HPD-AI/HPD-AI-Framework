namespace HPD.Base.Application.Generation;

/// <summary>
/// Declares an application index for a generated BASE collection contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BaseIndexAttribute(
    string id,
    params string[] fields) : Attribute
{
    /// <summary>
    /// Gets the stable index identifier.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets the CLR property names included in the index, in order.
    /// </summary>
    public IReadOnlyList<string> Fields { get; } = fields;

    /// <summary>
    /// Gets or sets whether the index enforces uniqueness.
    /// </summary>
    public bool Unique { get; set; }

    /// <summary>
    /// Gets or sets whether provider enforcement is required.
    /// </summary>
    public bool Required { get; set; } = true;
}
