using System.Text.Json.Serialization;

namespace HPD.RAG.RerankerProviders.Jina;

/// <summary>
/// Jina AI-specific reranker configuration.
/// Serialized into RerankerConfig.ProviderOptions for AOT-safe roundtripping.
/// </summary>
public sealed class JinaRerankerConfig
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    [JsonPropertyName("modelName")]
    public string? ModelName { get; set; }
}
