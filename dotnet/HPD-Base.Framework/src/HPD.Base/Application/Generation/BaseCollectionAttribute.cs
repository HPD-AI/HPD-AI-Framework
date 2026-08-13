namespace HPD.Base;

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

    /// <summary>Gets or sets the authoritative collection mutation mode.</summary>
    public BaseCollectionMutationMode MutationMode { get; set; } = BaseCollectionMutationMode.Mutable;

    /// <summary>
    /// Gets or sets the installed module identifier that owns this private system
    /// collection. A non-null value makes the generated collection non-exposed and
    /// subject to the exact L42 system-collection grant boundary.
    /// </summary>
    public string? SystemOwnerModuleId { get; set; }
}
