using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// DashScope-specific provider configuration.
/// These options map to Microsoft.Extensions.AI options supported by the Cnblogs DashScope adapter.
/// </summary>
public class DashScopeProviderConfig
{
    /// <summary>
    /// DashScope HTTP API base address.
    /// </summary>
    [JsonPropertyName("baseAddress")]
    public string? BaseAddress { get; set; }

    /// <summary>
    /// DashScope websocket API base address.
    /// </summary>
    [JsonPropertyName("websocketBaseAddress")]
    public string? WebsocketBaseAddress { get; set; }

    /// <summary>
    /// DashScope workspace id used for requests that require workspace scoping.
    /// </summary>
    [JsonPropertyName("workspaceId")]
    public string? WorkspaceId { get; set; }

    /// <summary>
    /// Internal websocket pool size used by the DashScope SDK.
    /// </summary>
    [JsonPropertyName("socketPoolSize")]
    public int? SocketPoolSize { get; set; }

    /// <summary>
    /// Request timeout in seconds for the underlying DashScope HTTP client.
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Forces use of DashScope multimodal generation endpoints.
    /// If null, the adapter infers this from the model id.
    /// </summary>
    [JsonPropertyName("useVl")]
    public bool? UseVl { get; set; }

    /// <summary>
    /// Controls randomness. Valid range: 0.0 to 2.0.
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
    /// Seed for deterministic generation. Must be non-negative.
    /// </summary>
    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    /// <summary>
    /// Character sequences that stop generation.
    /// </summary>
    [JsonPropertyName("stopSequences")]
    public List<string>? StopSequences { get; set; }

    /// <summary>
    /// Default embedding model.
    /// </summary>
    [JsonPropertyName("embeddingModelId")]
    public string? EmbeddingModelId { get; set; }

    /// <summary>
    /// Optional embedding dimensions.
    /// </summary>
    [JsonPropertyName("embeddingDimensions")]
    public int? EmbeddingDimensions { get; set; }
}
