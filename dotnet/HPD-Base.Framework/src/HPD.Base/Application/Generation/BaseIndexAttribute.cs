namespace HPD.Base;

/// <summary>
/// Declares an application index for a generated BASE collection contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BaseIndexAttribute(
    string id) : Attribute
{
    /// <summary>
    /// Gets the stable index identifier.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets the positive logical index version.
    /// </summary>
    public long Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets whether the index enforces uniqueness.
    /// </summary>
    public bool Unique { get; set; }

    /// <summary>
    /// Gets or sets whether provider enforcement is required.
    /// </summary>
    public bool StoreRequired { get; set; } = true;

}
