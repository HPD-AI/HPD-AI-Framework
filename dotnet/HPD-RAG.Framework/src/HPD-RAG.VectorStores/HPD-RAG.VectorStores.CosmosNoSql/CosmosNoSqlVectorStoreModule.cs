using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.CosmosNoSql;

/// <summary>
/// Self-registers the Azure Cosmos DB NoSQL vector store provider on assembly load.
/// </summary>
public static class CosmosNoSqlVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new CosmosNoSqlVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<CosmosNoSqlVectorStoreConfig>(
            "cosmos-nosql",
            json => JsonSerializer.Deserialize(json, CosmosNoSqlVectorStoreJsonContext.Default.CosmosNoSqlVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, CosmosNoSqlVectorStoreJsonContext.Default.CosmosNoSqlVectorStoreConfig));
    }
}
