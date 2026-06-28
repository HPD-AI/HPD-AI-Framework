using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// Together AI-specific provider configuration.
/// </summary>
public class TogetherProviderConfig
{
    /// <summary>
    /// Default embedding model.
    /// </summary>
    [JsonPropertyName("embeddingModelId")]
    public string? EmbeddingModelId { get; set; }
}
