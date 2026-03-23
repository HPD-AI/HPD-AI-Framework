namespace HPD.RAG.VectorStores.Weaviate;

/// <summary>
/// Weaviate-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class WeaviateVectorStoreConfig
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
}
