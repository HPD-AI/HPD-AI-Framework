using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.SqlServer;

/// <summary>
/// Self-registers the SQL Server vector store provider on assembly load.
/// </summary>
public static class SqlServerVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new SqlServerVectorStoreFeatures());
        VectorStoreDiscovery.RegisterVectorStoreConfigType<SqlServerVectorStoreConfig>(
            "sqlserver",
            json => JsonSerializer.Deserialize(json, SqlServerVectorStoreJsonContext.Default.SqlServerVectorStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, SqlServerVectorStoreJsonContext.Default.SqlServerVectorStoreConfig));
    }
}
