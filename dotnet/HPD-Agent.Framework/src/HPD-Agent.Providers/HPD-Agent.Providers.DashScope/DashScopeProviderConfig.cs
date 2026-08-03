using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// DashScope-specific provider configuration.
/// </summary>
public class DashScopeProviderConfig : global::HPD.Agent.IEmbeddingGenerationProviderOptions
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
    [JsonPropertyName("defaultUseVl")]
    public bool? DefaultUseVl { get; set; }

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
