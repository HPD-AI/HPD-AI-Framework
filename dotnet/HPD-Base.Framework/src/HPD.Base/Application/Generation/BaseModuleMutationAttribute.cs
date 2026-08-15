namespace HPD.Base;

/// <summary>Declares one generated Service/System registered module mutation identity.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BaseRegisteredModuleMutationAttribute(
    string id,
    Type jsonContextType,
    Type requestType,
    Type resultType) : Attribute
{
    /// <summary>Gets the stable operation identifier.</summary>
    public string Id { get; } = id;
    /// <summary>Gets the application-owned source-generated serializer context.</summary>
    public Type JsonContextType { get; } = jsonContextType;
    /// <summary>Gets the exact request DTO type.</summary>
    public Type RequestType { get; } = requestType;
    /// <summary>Gets the exact result DTO type.</summary>
    public Type ResultType { get; } = resultType;
    /// <summary>Gets or sets the positive operation version.</summary>
    public int Version { get; set; } = 1;
    /// <summary>Gets or sets the owning module identity.</summary>
    public string OwningModuleId { get; set; } = string.Empty;
    /// <summary>Gets or sets the exact execution grant identity.</summary>
    public string GrantId { get; set; } = string.Empty;
}
