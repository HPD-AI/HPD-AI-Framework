using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Cohere;

/// <summary>
/// Cohere-specific provider configuration.
/// </summary>
public class CohereProviderConfig : global::HPD.Agent.IEmbeddingGenerationProviderOptions
{
    /// <summary>
    /// Default embedding model.
    /// </summary>
    [JsonPropertyName("embeddingModelId")]
    public string? EmbeddingModelId { get; set; }
}
