using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.SemanticKernel.Connectors.MongoDB;
using MongoDB.Driver;

namespace HPD.RAG.VectorStores.Mongo;

internal sealed class MongoVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "mongo";
    public string DisplayName => "MongoDB Atlas";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<MongoVectorStoreConfig>();
        var connectionString = typed?.ConnectionString ?? config.ConnectionString
            ?? throw new InvalidOperationException("MongoDB connection string is required.");
        var databaseName = typed?.DatabaseName ?? "mrag";

        var mongoClient = new MongoClient(connectionString);
        var database = mongoClient.GetDatabase(databaseName);
        return new MongoVectorStore(database);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new MongoMragFilterTranslator();
}
