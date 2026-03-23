using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Weaviate;

/// <summary>
/// Self-registers the Weaviate vector store provider on assembly load.
/// </summary>
public static class WeaviateVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new WeaviateVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<WeaviateVectorStoreConfig>(
            "weaviate",
            json => JsonSerializer.Deserialize(json, WeaviateVectorStoreJsonContext.Default.WeaviateVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, WeaviateVectorStoreJsonContext.Default.WeaviateVectorStoreConfig));
    }
}
