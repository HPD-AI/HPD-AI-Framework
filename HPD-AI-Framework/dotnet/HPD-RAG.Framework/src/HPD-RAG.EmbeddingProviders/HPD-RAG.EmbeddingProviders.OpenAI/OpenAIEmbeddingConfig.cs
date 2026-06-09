using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.OpenAI;

/// <summary>
/// OpenAI-specific embedding configuration.
/// Serialized into EmbeddingConfig.ProviderOptions for AOT-safe roundtripping.
/// </summary>
public sealed class OpenAIEmbeddingConfig
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }
}
