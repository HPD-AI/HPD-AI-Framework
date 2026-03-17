namespace HPD.RAG.VectorStores.SqlServer;

/// <summary>
/// SQL Server-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class SqlServerVectorStoreConfig
{
    public string? ConnectionString { get; set; }
    public string Schema { get; set; } = "dbo";
}
