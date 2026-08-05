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

    /// <summary>Gets or sets the HTTP exposure of the generated read.</summary>
    public BaseReadExposure Exposure { get; set; }

    /// <summary>Gets or sets the minimum principal authorization required to invoke the read.</summary>
    public BaseReadAuthorization Authorization { get; set; } = BaseReadAuthorization.Authenticated;
}

/// <summary>Controls whether and where a registered read is exposed over HTTP.</summary>
public enum BaseReadExposure
{
    /// <summary>The read remains available only through typed in-process application APIs.</summary>
    None,
    /// <summary>The read is exposed on the public registered-read route surface.</summary>
    Public,
    /// <summary>The read is exposed only on the administrator registered-read route surface.</summary>
    Admin,
}

/// <summary>Defines the minimum trusted principal state required to invoke a registered read.</summary>
public enum BaseReadAuthorization
{
    /// <summary>An authenticated user, service, administrator, or system principal may invoke the read.</summary>
    Authenticated,
    /// <summary>Only an administrator or system principal may invoke the read.</summary>
    Admin,
    /// <summary>Only a system principal may invoke the read.</summary>
    System,
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
