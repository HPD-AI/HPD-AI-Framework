using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Pinecone;

/// <summary>
/// Self-registers the Pinecone vector store provider on assembly load.
/// </summary>
public static class PineconeVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new PineconeVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<PineconeVectorStoreConfig>(
            "pinecone",
            json => JsonSerializer.Deserialize(json, PineconeVectorStoreJsonContext.Default.PineconeVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, PineconeVectorStoreJsonContext.Default.PineconeVectorStoreConfig));
    }
}
