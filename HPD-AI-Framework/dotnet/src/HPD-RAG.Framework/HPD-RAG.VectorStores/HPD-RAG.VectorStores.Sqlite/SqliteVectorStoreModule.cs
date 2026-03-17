using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Sqlite;

/// <summary>
/// Self-registers the SQLite vector store provider on assembly load.
/// </summary>
public static class SqliteVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new SqliteVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<SqliteVectorStoreConfig>(
            "sqlite",
            json => JsonSerializer.Deserialize(json, SqliteVectorStoreJsonContext.Default.SqliteVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, SqliteVectorStoreJsonContext.Default.SqliteVectorStoreConfig));
    }
}
