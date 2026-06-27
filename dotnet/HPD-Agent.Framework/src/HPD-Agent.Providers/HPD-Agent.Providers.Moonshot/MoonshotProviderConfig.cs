using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Moonshot;

/// <summary>
/// Moonshot-specific chat defaults for the OpenAI-compatible chat-completions endpoint.
/// </summary>
public class MoonshotProviderConfig
{
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("topP")]
    public float? TopP { get; set; }

    [JsonPropertyName("maxOutputTokens")]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("stopSequences")]
    public List<string>? StopSequences { get; set; }

    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>
    /// Response format. Supported values: text, json_object.
    /// JSON schema response format can still be supplied per request via ChatOptions.
    /// </summary>
    [JsonPropertyName("responseFormat")]
    public string? ResponseFormat { get; set; }

    /// <summary>
    /// Tool choice behavior. Supported values: auto, none, required.
    /// </summary>
    [JsonPropertyName("toolChoice")]
    public string? ToolChoice { get; set; }

    /// <summary>
    /// Optional Kimi thinking control. Supported values: enabled, disabled.
    /// </summary>
    [JsonPropertyName("thinkingType")]
    public string? ThinkingType { get; set; }

    /// <summary>
    /// Optional Kimi preserved-thinking control. Supported value: all.
    /// </summary>
    [JsonPropertyName("thinkingKeep")]
    public string? ThinkingKeep { get; set; }
}
