using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;
/// <summary>Entry point for explicit dynamic and framework collection declarations.</summary>
public static class BaseCollection
{
    /// <summary>Performs define.</summary>
    public static BaseCollection<T> Define<T>(string id, JsonTypeInfo<T> jsonTypeInfo, Action<BaseCollectionSchemaBuilder<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new BaseCollectionSchemaBuilder<T>(id, jsonTypeInfo);
        configure(builder);
        return builder.Build();
    }
}
