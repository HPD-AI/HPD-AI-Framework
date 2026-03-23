using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Milvus;

/// <summary>
/// Self-registers the Milvus vector store provider on assembly load.
/// </summary>
public static class MilvusVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new MilvusVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<MilvusVectorStoreConfig>(
            "milvus",
            json => JsonSerializer.Deserialize(json, MilvusVectorStoreJsonContext.Default.MilvusVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, MilvusVectorStoreJsonContext.Default.MilvusVectorStoreConfig));
    }
}
