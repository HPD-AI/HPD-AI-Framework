using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Cohere;

/// <summary>
/// Cohere-specific provider configuration.
/// </summary>
public class CohereProviderConfig
{
    /// <summary>
    /// Default embedding model.
    /// </summary>
    [JsonPropertyName("embeddingModelId")]
    public string? EmbeddingModelId { get; set; }
}
