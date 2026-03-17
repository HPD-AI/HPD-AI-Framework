using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Redis;

/// <summary>
/// Self-registers the Redis vector store provider on assembly load.
/// </summary>
public static class RedisVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new RedisVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<RedisVectorStoreConfig>(
            "redis",
            json => JsonSerializer.Deserialize(json, RedisVectorStoreJsonContext.Default.RedisVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, RedisVectorStoreJsonContext.Default.RedisVectorStoreConfig));
    }
}
