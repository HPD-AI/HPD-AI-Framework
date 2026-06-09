namespace HPD.RAG.RerankerProviders.HuggingFace;

/// <summary>
/// HuggingFace TEI-specific reranker configuration.
/// Serialized into RerankerConfig.ProviderOptions for AOT-safe roundtripping.
/// </summary>
public sealed class HuggingFaceRerankerConfig
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
}
