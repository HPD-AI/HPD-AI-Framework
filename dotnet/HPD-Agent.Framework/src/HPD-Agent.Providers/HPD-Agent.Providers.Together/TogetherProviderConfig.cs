using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// Together AI-specific provider configuration.
/// </summary>
public class TogetherProviderConfig : global::HPD.Agent.IEmbeddingGenerationProviderOptions
{
    /// <summary>
    /// Default embedding model.
    /// </summary>
    [JsonPropertyName("embeddingModelId")]
    public string? EmbeddingModelId { get; set; }
}
