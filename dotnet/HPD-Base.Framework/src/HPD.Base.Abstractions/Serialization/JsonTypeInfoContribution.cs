using System.Text.Json.Serialization.Metadata;

namespace HPD.Base.Serialization;

public interface IBaseJsonTypeInfoContributor
{
    string Id { get; }
    string Version { get; }
    void AddTo(IBaseJsonTypeInfoRegistry registry);
}

public interface IBaseJsonTypeInfoRegistry
{
    void AddResolver(string contributorId, IJsonTypeInfoResolver resolver);
    void AddTypeInfo<T>(string contributorId, JsonTypeInfo<T> typeInfo);
}
