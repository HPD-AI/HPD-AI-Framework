using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

/// <summary>Defines the ibase JSON type info contributor contract.</summary>
public interface IBaseJsonTypeInfoContributor
{
    /// <summary>Gets the ID.</summary>
    string Id { get; }
    /// <summary>Gets the version.</summary>
    string Version { get; }
    /// <summary>Executes the add to operation.</summary>
    void AddTo(IBaseJsonTypeInfoRegistry registry);
}

/// <summary>Defines the ibase JSON type info registry contract.</summary>
public interface IBaseJsonTypeInfoRegistry
{
    /// <summary>Executes the add resolver operation.</summary>
    void AddResolver(string contributorId, IJsonTypeInfoResolver resolver);
    /// <summary>Executes the add type info operation.</summary>
    void AddTypeInfo<T>(string contributorId, JsonTypeInfo<T> typeInfo);
}
