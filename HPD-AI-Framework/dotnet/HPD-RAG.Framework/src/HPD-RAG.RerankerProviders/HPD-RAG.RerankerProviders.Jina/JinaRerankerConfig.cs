namespace HPD.RAG.RerankerProviders.Jina;

/// <summary>
/// Jina AI-specific reranker configuration.
/// Serialized into RerankerConfig.ProviderOptions for AOT-safe roundtripping.
/// </summary>
public sealed class JinaRerankerConfig
{
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? ModelName { get; set; }
}
