using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.SemanticKernel.Connectors.Qdrant;
using QdrantClient = Qdrant.Client.QdrantClient;

namespace HPD.RAG.VectorStores.Qdrant;

internal sealed class QdrantVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "qdrant";
    public string DisplayName => "Qdrant";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<QdrantVectorStoreConfig>();
        var endpoint = typed?.Endpoint ?? config.Endpoint
            ?? throw new InvalidOperationException("Qdrant endpoint is required.");
        var apiKey = typed?.ApiKey ?? config.ApiKey;

        var uri = new Uri(endpoint);
        var client = new QdrantClient(
            host: uri.Host,
            port: typed?.Port ?? 6334,
            https: typed?.UseHttps ?? false,
            apiKey: apiKey);

        return new QdrantVectorStore(client, ownsClient: true);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new QdrantMragFilterTranslator();
}
