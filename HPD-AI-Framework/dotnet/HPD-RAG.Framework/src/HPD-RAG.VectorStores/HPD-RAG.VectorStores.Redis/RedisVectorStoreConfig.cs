namespace HPD.RAG.VectorStores.Redis;

/// <summary>
/// Redis-specific typed config.
/// Serialized into VectorStoreConfig.ProviderOptions for AOT-safe roundtripping.
/// </summary>
public sealed class RedisVectorStoreConfig
{
    public string? ConnectionString { get; set; }
}
