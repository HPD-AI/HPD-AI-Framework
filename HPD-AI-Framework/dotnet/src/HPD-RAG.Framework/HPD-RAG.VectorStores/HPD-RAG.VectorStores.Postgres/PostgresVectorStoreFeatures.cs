using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Npgsql;

namespace HPD.RAG.VectorStores.Postgres;

internal sealed class PostgresVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "postgres";
    public string DisplayName => "PostgreSQL (pgvector)";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<PostgresVectorStoreConfig>();
        var connectionString = typed?.ConnectionString ?? config.ConnectionString
            ?? throw new InvalidOperationException("Postgres connection string is required.");
        var schema = typed?.Schema ?? "public";

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        return new PostgresVectorStoreAdapter(dataSource, schema);
    }

    public IMragFilterTranslator CreateFilterTranslator() => new PostgresMragFilterTranslator();
}
