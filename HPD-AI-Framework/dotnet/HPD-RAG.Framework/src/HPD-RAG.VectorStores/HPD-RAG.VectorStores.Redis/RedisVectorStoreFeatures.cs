using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.SemanticKernel.Connectors.Redis;
using StackExchange.Redis;

namespace HPD.RAG.VectorStores.Redis;

internal sealed class RedisVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "redis";
    public string DisplayName => "Redis (RediSearch)";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<RedisVectorStoreConfig>();
        var connectionString = typed?.ConnectionString ?? config.ConnectionString
            ?? throw new InvalidOperationException("Redis connection string is required.");

        var muxer = ConnectionMultiplexer.Connect(connectionString);
        return new RedisVectorStore(muxer.GetDatabase());
    }

    public IMragFilterTranslator CreateFilterTranslator() => new RedisMragFilterTranslator();
}
