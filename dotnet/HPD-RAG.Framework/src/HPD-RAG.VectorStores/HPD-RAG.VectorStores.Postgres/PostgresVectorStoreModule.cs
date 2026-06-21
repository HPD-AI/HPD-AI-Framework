using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Postgres;

/// <summary>
/// Self-registers the Postgres vector store provider on assembly load.
/// </summary>
public static class PostgresVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new PostgresVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<PostgresVectorStoreConfig>(
            "postgres",
            json => JsonSerializer.Deserialize(json, PostgresVectorStoreJsonContext.Default.PostgresVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, PostgresVectorStoreJsonContext.Default.PostgresVectorStoreConfig));
    }
}
