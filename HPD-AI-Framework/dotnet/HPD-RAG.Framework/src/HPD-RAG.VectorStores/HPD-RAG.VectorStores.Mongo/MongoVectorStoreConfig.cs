namespace HPD.RAG.VectorStores.Mongo;

/// <summary>
/// MongoDB-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class MongoVectorStoreConfig
{
    public string? ConnectionString { get; set; }
    public string DatabaseName { get; set; } = "mrag";
}
