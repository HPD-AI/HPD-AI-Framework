namespace HPD.RAG.VectorStores.CosmosMongo;

/// <summary>
/// Azure Cosmos DB for MongoDB-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class CosmosMongoVectorStoreConfig
{
    public string? ConnectionString { get; set; }
    public string DatabaseName { get; set; } = "mrag";
}
