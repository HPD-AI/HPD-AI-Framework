using Azure;
using Azure.Search.Documents.Indexes;
using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.SemanticKernel.Connectors.AzureAISearch;

namespace HPD.RAG.VectorStores.AzureAISearch;

internal sealed class AzureAISearchVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "azure-ai-search";
    public string DisplayName => "Azure AI Search";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<AzureAISearchVectorStoreConfig>();
        var endpoint = typed?.Endpoint
            ?? throw new InvalidOperationException("Azure AI Search endpoint is required in AzureAISearchVectorStoreConfig.ProviderOptions.");
        var apiKey = typed?.ApiKey
            ?? throw new InvalidOperationException("Azure AI Search API key is required in AzureAISearchVectorStoreConfig.ProviderOptions.");

        var searchClient = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        return new AzureAISearchVectorStore(searchClient);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new AzureAISearchMragFilterTranslator();
}
