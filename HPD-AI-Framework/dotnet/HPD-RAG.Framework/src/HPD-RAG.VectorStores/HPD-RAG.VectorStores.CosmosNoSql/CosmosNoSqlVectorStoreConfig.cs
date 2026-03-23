namespace HPD.RAG.VectorStores.CosmosNoSql;

/// <summary>
/// Azure Cosmos DB NoSQL-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class CosmosNoSqlVectorStoreConfig
{
    public string? ConnectionString { get; set; }
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string DatabaseName { get; set; } = "mrag";
}
