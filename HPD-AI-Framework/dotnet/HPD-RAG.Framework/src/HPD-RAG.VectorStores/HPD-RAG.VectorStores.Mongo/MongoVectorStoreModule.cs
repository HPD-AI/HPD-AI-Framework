using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Mongo;

/// <summary>
/// Self-registers the MongoDB vector store provider on assembly load.
/// </summary>
public static class MongoVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new MongoVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<MongoVectorStoreConfig>(
            "mongo",
            json => JsonSerializer.Deserialize(json, MongoVectorStoreJsonContext.Default.MongoVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, MongoVectorStoreJsonContext.Default.MongoVectorStoreConfig));
    }
}
