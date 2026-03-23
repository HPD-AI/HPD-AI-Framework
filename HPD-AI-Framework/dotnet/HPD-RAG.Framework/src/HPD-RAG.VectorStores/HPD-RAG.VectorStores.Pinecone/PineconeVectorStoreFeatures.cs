using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.SemanticKernel.Connectors.Pinecone;

namespace HPD.RAG.VectorStores.Pinecone;

internal sealed class PineconeVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "pinecone";
    public string DisplayName => "Pinecone";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<PineconeVectorStoreConfig>();
        var apiKey = typed?.ApiKey ?? config.ApiKey
            ?? throw new InvalidOperationException("Pinecone API key is required.");

        var pineconeClient = new global::Pinecone.PineconeClient(apiKey);
        return new PineconeVectorStore(pineconeClient);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new PineconeMragFilterTranslator();
}
