using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;

// Alias to avoid conflict with our own namespace segment "Milvus"
using MilvusClientType = Milvus.Client.MilvusClient;

namespace HPD.RAG.VectorStores.Milvus;

internal sealed class MilvusVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "milvus";
    public string DisplayName => "Milvus";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<MilvusVectorStoreConfig>();
        var host = typed?.Host ?? "localhost";
        var port = typed?.Port ?? 19530;
        var apiKey = typed?.ApiKey ?? config.ApiKey;
        var useTls = typed?.UseTls ?? false;

        // MilvusClient(host, port, ssl, apiKey, ...)
        var milvusClient = new MilvusClientType(
            host,
            port,
            useTls,
            apiKey ?? string.Empty);

        return new MilvusVectorStoreAdapter(milvusClient);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new MilvusMragFilterTranslator();
}
