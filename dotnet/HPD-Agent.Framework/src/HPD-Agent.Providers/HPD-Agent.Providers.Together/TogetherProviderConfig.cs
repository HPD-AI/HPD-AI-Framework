using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// Together AI-specific provider configuration.
/// </summary>
public class TogetherProviderConfig
{
    /// <summary>
    /// Controls output randomness. Valid range: 0.0 to 2.0.
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    /// <summary>
    /// Nucleus sampling probability mass. Valid range: 0.0 to 1.0.
    /// </summary>
    [JsonPropertyName("topP")]
    public double? TopP { get; set; }

    /// <summary>
    /// Maximum number of tokens to consider at each generation step. Must be greater than 0.
    /// </summary>
    [JsonPropertyName("topK")]
    public int? TopK { get; set; }

    /// <summary>
    /// Maximum number of output tokens to generate. Must be greater than 0.
    /// </summary>
    [JsonPropertyName("maxOutputTokens")]
    public int? MaxOutputTokens { get; set; }

    /// <summary>
    /// Seed for deterministic generation.
    /// </summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>
    /// Character sequences that stop generation.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public List<string>? StopSequences { get; set; }

    /// <summary>
    /// Response format. Supported values: text, json_object.
    /// </summary>
    [JsonPropertyName("responseFormat")]
    public string? ResponseFormat { get; set; }

    /// <summary>
    /// Default embedding model.
    /// </summary>
    [JsonPropertyName("embeddingModelId")]
    public string? EmbeddingModelId { get; set; }
}
