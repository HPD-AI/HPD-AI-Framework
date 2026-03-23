namespace HPD.RAG.VectorStores.Milvus;

/// <summary>
/// Milvus-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class MilvusVectorStoreConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 19530;
    public string? ApiKey { get; set; }
    public bool UseTls { get; set; } = false;
    public string? DatabaseName { get; set; }
}
