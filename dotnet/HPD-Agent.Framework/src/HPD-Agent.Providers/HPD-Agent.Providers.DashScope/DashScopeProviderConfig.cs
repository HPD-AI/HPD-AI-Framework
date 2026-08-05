using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// DashScope-specific provider configuration.
/// </summary>
public sealed class DashScopeProviderConfig : global::HPD.Agent.IProviderConfig
{
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

}
