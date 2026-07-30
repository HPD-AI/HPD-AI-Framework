namespace HPD.Base.Application.Generation;

/// <summary>
/// Marks a partial record or class as a generated BASE collection contract.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BaseCollectionAttribute(
    string id,
    Type jsonContextType) : Attribute
{
    /// <summary>
    /// Gets the canonical collection identifier.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets the application-owned source-generated JSON context type.
    /// </summary>
    public Type JsonContextType { get; } = jsonContextType;

    /// <summary>
    /// Gets or sets the descriptive collection name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the collection kind.
    /// </summary>
    public string Kind { get; set; } = "record";

    /// <summary>
    /// Gets or sets whether unknown stored fields are rejected.
    /// </summary>
    public bool Strict { get; set; } = true;
}
