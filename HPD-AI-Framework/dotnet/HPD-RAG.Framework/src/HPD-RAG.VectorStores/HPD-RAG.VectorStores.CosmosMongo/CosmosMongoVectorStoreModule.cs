using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.CosmosMongo;

/// <summary>
/// Self-registers the Azure Cosmos DB for MongoDB vector store provider on assembly load.
/// </summary>
public static class CosmosMongoVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new CosmosMongoVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<CosmosMongoVectorStoreConfig>(
            "cosmos-mongo",
            json => JsonSerializer.Deserialize(json, CosmosMongoVectorStoreJsonContext.Default.CosmosMongoVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, CosmosMongoVectorStoreJsonContext.Default.CosmosMongoVectorStoreConfig));
    }
}
