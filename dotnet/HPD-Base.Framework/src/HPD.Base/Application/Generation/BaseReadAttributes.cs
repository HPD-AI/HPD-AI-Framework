namespace HPD.Base;

/// <summary>Declares one generated, registered relational read.</summary>
/// <param name="id">The stable read-definition identifier.</param>
/// <param name="jsonContextType">The source-generated JSON context type.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BaseReadAttribute(string id, Type jsonContextType) : Attribute
{
    /// <summary>Gets the stable read-definition identifier.</summary>
    public string Id { get; } = id;

    /// <summary>Gets the source-generated JSON context type.</summary>
    public Type JsonContextType { get; } = jsonContextType;
}

/// <summary>Declares one stable generated read parameter.</summary>
/// <param name="id">The stable parameter identifier.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BaseReadParameterAttribute(string id) : Attribute
{
    /// <summary>Gets the stable parameter identifier.</summary>
    public string Id { get; } = id;
}

/// <summary>Declares one stable generated read projection field.</summary>
/// <param name="id">The stable projection-field identifier.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BaseReadFieldAttribute(string id) : Attribute
{
    /// <summary>Gets the stable projection-field identifier.</summary>
    public string Id { get; } = id;
}
