namespace HPD.RAG.VectorStores.Qdrant;

/// <summary>
/// Qdrant-specific typed config. Extends base endpoint and API key with port configuration.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class QdrantVectorStoreConfig
{
    public string? Endpoint { get; set; }
    public int Port { get; set; } = 6334;
    public string? ApiKey { get; set; }
    public bool UseHttps { get; set; } = false;
}
