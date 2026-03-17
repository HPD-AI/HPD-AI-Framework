using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using MongoDB.Driver;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.VectorStores.CosmosMongo;

internal sealed class CosmosMongoVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "cosmos-mongo";
    public string DisplayName => "Azure Cosmos DB for MongoDB";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<CosmosMongoVectorStoreConfig>();
        var connectionString = typed?.ConnectionString ?? config.ConnectionString
            ?? throw new InvalidOperationException("Cosmos DB for MongoDB connection string is required.");
        var databaseName = typed?.DatabaseName ?? "mrag";

        var mongoClient = new MongoClient(connectionString);
        var database = mongoClient.GetDatabase(databaseName);
        return new CosmosMongoVectorStoreAdapter(database);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new CosmosMongoMragFilterTranslator();
}
