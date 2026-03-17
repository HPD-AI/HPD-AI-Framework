using System.Text.Json.Serialization;

namespace HPD.Agent.MCP;

/// <summary>
/// Root configuration object for MCP manifest files
/// </summary>
public class MCPManifest
{
    [JsonPropertyName("servers")]
    public List<MCPServerConfig> Servers { get; set; } = new();
}

/// <summary>
/// Configuration for a single MCP server
/// </summary>
public class MCPServerConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public List<string> Arguments { get; set; } = new();

    /// <summary>
    /// Optional description for the MCP server container.
    /// If not provided, will attempt to extract from server's ServerInfo metadata.
    /// If both are unavailable, will auto-generate from function names.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Enable Collapsing for this MCP server's tools.
    /// When true, tools are grouped behind a container (e.g., MCP_filesystem).
    /// When false, tools are exposed directly (e.g., filesystem_read_file).
    /// If not specified, defaults to false (no Collapsing).
    /// </summary>
    [JsonPropertyName("enablecollapsing")]
    public bool? EnableCollapsing { get; set; }

    /// <summary>
    /// Whether tools from this MCP server require user permission before execution.
    /// When true, all tools from this server will trigger permission requests.
    /// When false (default), tools execute without permission prompts.
    /// Use [RequiresPermission] on the method to opt in (same as [AIFunction] and [Skill]).
    /// </summary>
    [JsonPropertyName("requiresPermission")]
    public bool RequiresPermission { get; set; } = false;

    /// <summary>
    /// Sandbox configuration for this MCP server.
    /// Controls filesystem and network restrictions when running the server process.
    /// </summary>
    /// <remarks>
    /// <para>If null, uses default restrictive sandbox (deny ~/.ssh, ~/.aws, no network).</para>
    /// <para>If <c>enabled: false</c>, runs without sandbox (use for trusted servers only).</para>
    /// </remarks>
    [JsonPropertyName("sandbox")]
    public MCPSandboxConfig? Sandbox { get; set; }

    /// <summary>
    /// Ephemeral instructions returned in function result when container is expanded (one-time).
    /// This is appended to the auto-generated expansion message.
    /// Use for additional context like working directory, connection info, or tips.
    /// </summary>
    [JsonPropertyName("functionResult")]
    public string? FunctionResult { get; set; }

    /// <summary>
    /// Persistent instructions injected into system prompt after expansion (every iteration).
    /// Use for critical rules, workflow guidance, and constraints.
    /// </summary>
    [JsonPropertyName("systemPrompt")]
    public string? SystemPrompt { get; set; }

    [JsonPropertyName("timeout")]
    public int TimeoutMs { get; set; } = 30000;
    
    [JsonPropertyName("retryAttempts")]
    public int RetryAttempts { get; set; } = 3;
    
    [JsonPropertyName("environment")]
    public Dictionary<string, string>? Environment { get; set; }

    // ========== Toolkit-Awareness Fields (set at runtime, not serialized from JSON) ==========

    /// <summary>
    /// Name of the parent toolkit that owns this MCP server (set via [MCPServer] attribute).
    /// Used at runtime to stamp ParentContainer on MCP tools for visibility management.
    /// Null for standalone MCP servers registered via WithMCP().
    /// </summary>
    [JsonIgnore]
    public string? ParentToolkit { get; set; }

    /// <summary>
    /// When true, MCP tools sit behind their own MCP_* container nested inside the parent toolkit.
    /// When false (default), tools appear directly under the parent toolkit on expansion.
    /// Only meaningful when ParentToolkit is set.
    /// </summary>
    [JsonIgnore]
    public bool CollapseWithinToolkit { get; set; }

    /// <summary>
    /// Validates the server configuration
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Server name is required", nameof(Name));

        if (string.IsNullOrWhiteSpace(Command))
            throw new ArgumentException("Server command is required", nameof(Command));

        if (TimeoutMs <= 0)
            throw new ArgumentException("Timeout must be positive", nameof(TimeoutMs));

        if (RetryAttempts < 0)
            throw new ArgumentException("Retry attempts cannot be negative", nameof(RetryAttempts));

        // Warn if instructions are provided but collapsing is disabled
        // (instructions only work when tools are grouped behind a container)
        if (EnableCollapsing == false && (!string.IsNullOrWhiteSpace(FunctionResult) || !string.IsNullOrWhiteSpace(SystemPrompt)))
        {
            throw new ArgumentException(
                $"Server '{Name}' has 'functionResult' or 'systemPrompt' but 'enablecollapsing' is false. " +
                "Instructions are only used when collapsing is enabled (tools are grouped behind a container). " +
                "Either set 'enablecollapsing: true' or remove the instructions.",
                nameof(EnableCollapsing));
        }
    }
}

/// <summary>
/// Options for configuring MCP integration
/// </summary>
public class MCPOptions
{
    public string ManifestPath { get; set; } = string.Empty;
    public bool FailOnServerError { get; set; } = false;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxConcurrentServers { get; set; } = 10;
}

/// <summary>
/// JSON serialization context for AOT compilation
/// </summary>
[JsonSerializable(typeof(MCPManifest))]
[JsonSerializable(typeof(MCPServerConfig))]
[JsonSerializable(typeof(MCPSandboxConfig))]
[JsonSerializable(typeof(List<MCPServerConfig>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class MCPJsonSerializerContext : JsonSerializerContext
{
}
