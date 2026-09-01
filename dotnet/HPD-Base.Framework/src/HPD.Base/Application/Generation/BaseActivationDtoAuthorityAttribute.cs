namespace HPD.Base;

/// <summary>Declares source-generated serializer and scalar authority for one durable activation DTO pair.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BaseActivationDtoAuthorityAttribute(
    string id,
    int version,
    string owningModuleId,
    string inputTypeId,
    string resultTypeId,
    Type jsonContextType,
    Type inputType,
    Type resultType) : Attribute
{
    /// <summary>Gets the stable DTO-authority identity.</summary>
    public string Id { get; } = id;
    /// <summary>Gets the positive DTO-authority version.</summary>
    public int Version { get; } = version;
    /// <summary>Gets the owning module identity.</summary>
    public string OwningModuleId { get; } = owningModuleId;
    /// <summary>Gets the stable input graph type identity.</summary>
    public string InputTypeId { get; } = inputTypeId;
    /// <summary>Gets the stable result graph type identity.</summary>
    public string ResultTypeId { get; } = resultTypeId;
    /// <summary>Gets the application-owned source-generated serializer context.</summary>
    public Type JsonContextType { get; } = jsonContextType;
    /// <summary>Gets the exact input DTO type.</summary>
    public Type InputType { get; } = inputType;
    /// <summary>Gets the exact result DTO type.</summary>
    public Type ResultType { get; } = resultType;
}
