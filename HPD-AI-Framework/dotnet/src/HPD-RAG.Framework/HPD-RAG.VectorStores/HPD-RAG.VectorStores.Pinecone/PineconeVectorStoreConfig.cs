namespace HPD.RAG.VectorStores.Pinecone;

/// <summary>
/// Pinecone-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptionsJson for AOT-safe roundtripping.
/// </summary>
public sealed class PineconeVectorStoreConfig
{
    public string? ApiKey { get; set; }
}
