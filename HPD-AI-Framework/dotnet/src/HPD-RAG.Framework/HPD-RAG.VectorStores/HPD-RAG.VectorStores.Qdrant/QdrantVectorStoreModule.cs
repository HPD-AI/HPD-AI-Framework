using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Qdrant;

/// <summary>
/// Self-registers the Qdrant vector store provider on assembly load.
/// </summary>
public static class QdrantVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new QdrantVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<QdrantVectorStoreConfig>(
            "qdrant",
            json => JsonSerializer.Deserialize(json, QdrantVectorStoreJsonContext.Default.QdrantVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, QdrantVectorStoreJsonContext.Default.QdrantVectorStoreConfig));
    }
}
