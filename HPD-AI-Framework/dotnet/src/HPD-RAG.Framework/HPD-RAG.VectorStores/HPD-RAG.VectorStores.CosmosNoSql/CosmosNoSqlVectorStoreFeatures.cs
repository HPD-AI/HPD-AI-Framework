using Azure;
using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.Azure.Cosmos;

namespace HPD.RAG.VectorStores.CosmosNoSql;

internal sealed class CosmosNoSqlVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "cosmos-nosql";
    public string DisplayName => "Azure Cosmos DB NoSQL";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<CosmosNoSqlVectorStoreConfig>();
        var databaseName = typed?.DatabaseName ?? "mrag";

        CosmosClient cosmosClient;
        var connectionString = typed?.ConnectionString ?? config.ConnectionString;
        if (!string.IsNullOrEmpty(connectionString))
        {
            cosmosClient = new CosmosClient(connectionString);
        }
        else
        {
            var endpoint = typed?.Endpoint ?? config.Endpoint
                ?? throw new InvalidOperationException("Cosmos DB NoSQL endpoint or connection string is required.");
            var apiKey = typed?.ApiKey ?? config.ApiKey
                ?? throw new InvalidOperationException("Cosmos DB NoSQL API key is required when endpoint is specified.");
            cosmosClient = new CosmosClient(endpoint, new AzureKeyCredential(apiKey));
        }

        var database = cosmosClient.GetDatabase(databaseName);
        return new CosmosNoSqlVectorStoreAdapter(cosmosClient, database);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new CosmosNoSqlMragFilterTranslator();
}
