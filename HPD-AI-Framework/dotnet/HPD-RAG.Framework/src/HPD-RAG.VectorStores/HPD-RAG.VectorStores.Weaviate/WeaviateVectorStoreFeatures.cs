using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.SemanticKernel.Connectors.Weaviate;

namespace HPD.RAG.VectorStores.Weaviate;

internal sealed class WeaviateVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "weaviate";
    public string DisplayName => "Weaviate";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<WeaviateVectorStoreConfig>();
        var endpoint = typed?.Endpoint ?? config.Endpoint
            ?? throw new InvalidOperationException("Weaviate endpoint is required.");
        var apiKey = typed?.ApiKey ?? config.ApiKey;

        var httpClient = new HttpClient();
        httpClient.BaseAddress = new Uri(endpoint);
        if (!string.IsNullOrEmpty(apiKey))
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        return new WeaviateVectorStore(httpClient);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new WeaviateMragFilterTranslator();
}
