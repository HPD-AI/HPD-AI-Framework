using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.Data.Sqlite;

namespace HPD.RAG.VectorStores.Sqlite;

internal sealed class SqliteVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "sqlite";
    public string DisplayName => "SQLite";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<SqliteVectorStoreConfig>();
        var databasePath = typed?.DatabasePath ?? config.ConnectionString
            ?? throw new InvalidOperationException("SQLite database path is required.");

        // Use raw SqliteConnection — SK Sqlite connector 1.51.0-preview uses incompatible MEVD 9.0.0-preview
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return new SqliteVectorStoreAdapter(connection);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new SqliteMragFilterTranslator();
}
