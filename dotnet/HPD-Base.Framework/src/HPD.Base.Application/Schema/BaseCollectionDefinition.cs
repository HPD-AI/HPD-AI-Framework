using System.Text.Json.Serialization.Metadata;
using HPD.Base.Application.Collections;

namespace HPD.Base.Application.Schema;

/// <summary>Entry point for explicit dynamic and framework collection declarations.</summary>
public static class BaseCollection
{
    public static BaseCollection<T> Define<T>(
        string id,
        JsonTypeInfo<T> jsonTypeInfo,
        Action<BaseCollectionSchemaBuilder<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new BaseCollectionSchemaBuilder<T>(id, jsonTypeInfo);
        configure(builder);
        return builder.Build();
    }
}
