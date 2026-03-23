using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.SemanticKernel.Connectors.SqlServer;

namespace HPD.RAG.VectorStores.SqlServer;

internal sealed class SqlServerVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "sqlserver";
    public string DisplayName => "SQL Server";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        var typed = config.GetTypedConfig<SqlServerVectorStoreConfig>();
        var connectionString = typed?.ConnectionString ?? config.ConnectionString
            ?? throw new InvalidOperationException("SQL Server connection string is required.");

        return new SqlServerVectorStore(connectionString, new SqlServerVectorStoreOptions
        {
            Schema = typed?.Schema ?? "dbo"
        });
    }

    public IMragFilterTranslator CreateFilterTranslator() => new SqlServerMragFilterTranslator();
}
