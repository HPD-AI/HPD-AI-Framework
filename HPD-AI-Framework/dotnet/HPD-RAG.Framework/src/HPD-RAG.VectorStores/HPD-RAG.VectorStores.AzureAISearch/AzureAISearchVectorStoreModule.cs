using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.AzureAISearch;

/// <summary>
/// Self-registers the Azure AI Search vector store provider on assembly load.
/// </summary>
public static class AzureAISearchVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new AzureAISearchVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<AzureAISearchVectorStoreConfig>(
            "azure-ai-search",
            json => JsonSerializer.Deserialize(json, AzureAISearchVectorStoreJsonContext.Default.AzureAISearchVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, AzureAISearchVectorStoreJsonContext.Default.AzureAISearchVectorStoreConfig));
    }
}
