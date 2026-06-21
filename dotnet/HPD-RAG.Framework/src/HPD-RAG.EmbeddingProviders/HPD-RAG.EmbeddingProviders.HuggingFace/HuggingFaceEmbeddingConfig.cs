using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.HuggingFace;

/// <summary>
/// HuggingFace Inference API-specific embedding configuration.
///
/// JSON Example (ProviderOptions):
/// <code>
/// {
///   "endpoint": "https://api-inference.huggingface.co",
///   "apiKey": "hf_..."
/// }
/// </code>
/// </summary>
public sealed class HuggingFaceEmbeddingConfig
{
    /// <summary>
    /// The HuggingFace Inference API base endpoint.
    /// Defaults to https://api-inference.huggingface.co.
    /// Override for self-hosted Inference Endpoints.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    /// <summary>
    /// HuggingFace API token.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }
}
