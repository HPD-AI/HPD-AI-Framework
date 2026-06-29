using System.Text.Json.Serialization;
using HPD.Environment.Contracts;

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

    [JsonPropertyName("transport")]
    public string Transport { get; set; } = string.Empty;

    // ========== stdio transport ==========

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("arguments")]
    public List<string> Arguments { get; set; } = new();

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("inheritEnvironmentVariables")]
    public bool InheritEnvironmentVariables { get; set; } = true;

    [JsonPropertyName("useDefaultEnvironmentVariables")]
    public bool UseDefaultEnvironmentVariables { get; set; }

    [JsonPropertyName("environment")]
    public Dictionary<string, string?>? Environment { get; set; }

    [JsonPropertyName("environmentSecretKeys")]
    public Dictionary<string, string>? EnvironmentSecretKeys { get; set; }

    [JsonPropertyName("processIsolation")]
    public MCPProcessIsolationConfig? ProcessIsolation { get; set; }

    // ========== HTTP transport ==========

    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    [JsonPropertyName("httpTransportMode")]
    public string? HttpTransportMode { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonPropertyName("headerSecretKeys")]
    public Dictionary<string, string>? HeaderSecretKeys { get; set; }

    [JsonPropertyName("knownSessionId")]
    public string? KnownSessionId { get; set; }

    [JsonPropertyName("ownsSession")]
    public bool OwnsSession { get; set; } = true;

    [JsonPropertyName("oauth")]
    public MCPOAuthConfig? OAuth { get; set; }

    // ========== MCP client options ==========

    [JsonPropertyName("clientName")]
    public string? ClientName { get; set; }

    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; set; }

    [JsonPropertyName("protocolVersion")]
    public string? ProtocolVersion { get; set; }

    [JsonPropertyName("connectionTimeoutMs")]
    public int ConnectionTimeoutMs { get; set; } = 30000;

    [JsonPropertyName("initializationTimeoutMs")]
    public int InitializationTimeoutMs { get; set; } = 60000;

    [JsonPropertyName("shutdownTimeoutMs")]
    public int ShutdownTimeoutMs { get; set; } = 5000;

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
    /// Expose MCP resources through generic HPD functions for this server.
    /// </summary>
    [JsonPropertyName("enableResources")]
    public bool EnableResources { get; set; }

    /// <summary>
    /// Maximum number of resources returned by a single list operation.
    /// </summary>
    [JsonPropertyName("maxResourceListResults")]
    public int MaxResourceListResults { get; set; } = 100;

    /// <summary>
    /// Maximum text characters returned by a single resource read operation.
    /// </summary>
    [JsonPropertyName("maxResourceContentLength")]
    public int MaxResourceContentLength { get; set; } = 200_000;

    /// <summary>
    /// Expose MCP prompts through generic HPD functions for this server.
    /// </summary>
    [JsonPropertyName("enablePrompts")]
    public bool EnablePrompts { get; set; }

    /// <summary>
    /// Listen for MCP server change notifications and emit HPD agent events.
    /// </summary>
    [JsonPropertyName("enableLiveUpdates")]
    public bool EnableLiveUpdates { get; set; }

    /// <summary>
    /// Specific MCP resource URIs to subscribe to for update notifications.
    /// Requires enableLiveUpdates.
    /// </summary>
    [JsonPropertyName("resourceSubscriptions")]
    public List<string> ResourceSubscriptions { get; set; } = new();

    /// <summary>
    /// Maximum number of prompts returned by a single list operation.
    /// </summary>
    [JsonPropertyName("maxPromptListResults")]
    public int MaxPromptListResults { get; set; } = 100;

    /// <summary>
    /// Maximum text characters returned by a single prompt get operation.
    /// </summary>
    [JsonPropertyName("maxPromptContentLength")]
    public int MaxPromptContentLength { get; set; } = 200_000;

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

    // ========== ToolHarness-Awareness Fields (set at runtime, not serialized from JSON) ==========

    /// <summary>
    /// Name of the parent toolharness that owns this MCP server (set via [MCPServer] attribute).
    /// Used at runtime to stamp ParentContainer on MCP tools for visibility management.
    /// Null for standalone MCP servers registered via WithMCP().
    /// </summary>
    [JsonIgnore]
    public string? ParentToolHarness { get; set; }

    /// <summary>
    /// When true, MCP tools sit behind their own MCP_* container nested inside the parent toolharness.
    /// When false (default), tools appear directly under the parent toolharness on expansion.
    /// Only meaningful when ParentToolHarness is set.
    /// </summary>
    [JsonIgnore]
    public bool CollapseWithinToolHarness { get; set; }

    /// <summary>
    /// Validates the server configuration
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Server name is required", nameof(Name));

        if (string.IsNullOrWhiteSpace(Transport))
            throw new ArgumentException("Server transport is required. Use 'stdio' or 'http'.", nameof(Transport));

        if (ConnectionTimeoutMs <= 0)
            throw new ArgumentException("Connection timeout must be positive", nameof(ConnectionTimeoutMs));

        if (InitializationTimeoutMs <= 0)
            throw new ArgumentException("Initialization timeout must be positive", nameof(InitializationTimeoutMs));

        if (ShutdownTimeoutMs <= 0)
            throw new ArgumentException("Shutdown timeout must be positive", nameof(ShutdownTimeoutMs));

        if (MaxResourceListResults <= 0)
            throw new ArgumentException("Max resource list results must be positive", nameof(MaxResourceListResults));

        if (MaxResourceContentLength <= 0)
            throw new ArgumentException("Max resource content length must be positive", nameof(MaxResourceContentLength));

        if (MaxPromptListResults <= 0)
            throw new ArgumentException("Max prompt list results must be positive", nameof(MaxPromptListResults));

        if (MaxPromptContentLength <= 0)
            throw new ArgumentException("Max prompt content length must be positive", nameof(MaxPromptContentLength));

        if (!EnableLiveUpdates && ResourceSubscriptions.Count > 0)
            throw new ArgumentException("Resource subscriptions require enableLiveUpdates", nameof(ResourceSubscriptions));

        if (ResourceSubscriptions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Resource subscription URIs cannot be empty", nameof(ResourceSubscriptions));

        if (!IsHttpTransport() && OAuth != null)
            throw new ArgumentException("OAuth is only supported for HTTP MCP servers", nameof(OAuth));

        if (!IsStdioTransport() && ProcessIsolation != null)
            throw new ArgumentException("Process isolation is only supported for stdio MCP servers", nameof(ProcessIsolation));

        if (IsStdioTransport())
        {
            if (string.IsNullOrWhiteSpace(Command))
                throw new ArgumentException("Stdio MCP servers require 'command'", nameof(Command));
        }
        else if (IsHttpTransport())
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
                throw new ArgumentException("HTTP MCP servers require 'endpoint'", nameof(Endpoint));

            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("HTTP MCP server endpoint must be an absolute HTTP or HTTPS URI", nameof(Endpoint));
            }

            OAuth?.Validate();
        }
        else
        {
            throw new ArgumentException($"Unsupported MCP transport '{Transport}'. Use 'stdio' or 'http'.", nameof(Transport));
        }

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

    public bool IsStdioTransport() => string.Equals(Transport, "stdio", StringComparison.OrdinalIgnoreCase);

    public bool IsHttpTransport() => string.Equals(Transport, "http", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Options for configuring MCP integration
/// </summary>
public class MCPOptions
{
    public bool FailOnServerError { get; set; } = false;

    public bool FailOnLiveUpdateError { get; set; } = false;

    /// <summary>
    /// Application-owned process provider used to launch isolated stdio MCP server processes.
    /// Required when an MCP server manifest requests processIsolation with mode isolated.
    /// </summary>
    public IProcessProvider? ProcessProvider { get; set; }

    /// <summary>
    /// Application-owned OAuth runtime hooks for HTTP MCP servers.
    /// </summary>
    public IMcpOAuthRuntime? OAuthRuntime { get; set; }
}

/// <summary>
/// OAuth configuration for an HTTP MCP server.
/// </summary>
public sealed class MCPOAuthConfig
{
    [JsonPropertyName("redirectUri")]
    public string RedirectUri { get; set; } = string.Empty;

    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("clientSecretKey")]
    public string? ClientSecretKey { get; set; }

    [JsonPropertyName("clientMetadataDocumentUri")]
    public string? ClientMetadataDocumentUri { get; set; }

    [JsonPropertyName("scopes")]
    public List<string>? Scopes { get; set; }

    [JsonPropertyName("additionalAuthorizationParameters")]
    public Dictionary<string, string>? AdditionalAuthorizationParameters { get; set; }

    [JsonPropertyName("dynamicClientRegistration")]
    public MCPDynamicClientRegistrationConfig? DynamicClientRegistration { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RedirectUri))
            throw new ArgumentException("OAuth redirectUri is required", nameof(RedirectUri));

        if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out _))
            throw new ArgumentException("OAuth redirectUri must be an absolute URI", nameof(RedirectUri));

        if (!string.IsNullOrWhiteSpace(ClientMetadataDocumentUri))
        {
            if (!Uri.TryCreate(ClientMetadataDocumentUri, UriKind.Absolute, out var metadataUri) ||
                metadataUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("OAuth clientMetadataDocumentUri must be an absolute HTTPS URI", nameof(ClientMetadataDocumentUri));
            }
        }

        DynamicClientRegistration?.Validate();
    }
}

/// <summary>
/// Dynamic client registration options for OAuth-protected HTTP MCP servers.
/// </summary>
public sealed class MCPDynamicClientRegistrationConfig
{
    [JsonPropertyName("clientName")]
    public string? ClientName { get; set; }

    [JsonPropertyName("clientUri")]
    public string? ClientUri { get; set; }

    [JsonPropertyName("initialAccessToken")]
    public string? InitialAccessToken { get; set; }

    [JsonPropertyName("initialAccessTokenKey")]
    public string? InitialAccessTokenKey { get; set; }

    public void Validate()
    {
        if (!string.IsNullOrWhiteSpace(ClientUri) &&
            (!Uri.TryCreate(ClientUri, UriKind.Absolute, out var clientUri) ||
             (clientUri.Scheme != Uri.UriSchemeHttp && clientUri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("Dynamic client registration clientUri must be an absolute HTTP or HTTPS URI", nameof(ClientUri));
        }
    }
}

/// <summary>
/// JSON serialization context for AOT compilation
/// </summary>
[JsonSerializable(typeof(MCPManifest))]
[JsonSerializable(typeof(MCPServerConfig))]
[JsonSerializable(typeof(MCPProcessIsolationConfig))]
[JsonSerializable(typeof(MCPOAuthConfig))]
[JsonSerializable(typeof(MCPDynamicClientRegistrationConfig))]
[JsonSerializable(typeof(MCPResourceListResult))]
[JsonSerializable(typeof(MCPResourceTemplateListResult))]
[JsonSerializable(typeof(MCPResourceReadResult))]
[JsonSerializable(typeof(MCPResourceSummary))]
[JsonSerializable(typeof(MCPResourceTemplateSummary))]
[JsonSerializable(typeof(MCPResourceContentSummary))]
[JsonSerializable(typeof(MCPPromptListResult))]
[JsonSerializable(typeof(MCPPromptGetResult))]
[JsonSerializable(typeof(MCPPromptSummary))]
[JsonSerializable(typeof(MCPPromptArgumentSummary))]
[JsonSerializable(typeof(MCPPromptMessageSummary))]
[JsonSerializable(typeof(MCPPromptContentSummary))]
[JsonSerializable(typeof(McpOAuthClientRegistration))]
[JsonSerializable(typeof(McpOAuthTokenCacheEntry))]
[JsonSerializable(typeof(List<MCPServerConfig>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<MCPResourceSummary>))]
[JsonSerializable(typeof(List<MCPResourceTemplateSummary>))]
[JsonSerializable(typeof(List<MCPResourceContentSummary>))]
[JsonSerializable(typeof(List<MCPPromptSummary>))]
[JsonSerializable(typeof(List<MCPPromptArgumentSummary>))]
[JsonSerializable(typeof(List<MCPPromptMessageSummary>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string?>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class MCPJsonSerializerContext : JsonSerializerContext
{
}

public sealed class MCPResourceListResult
{
    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("resources")]
    public List<MCPResourceSummary> Resources { get; set; } = new();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

public sealed class MCPResourceTemplateListResult
{
    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("resourceTemplates")]
    public List<MCPResourceTemplateSummary> ResourceTemplates { get; set; } = new();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

public sealed class MCPResourceReadResult
{
    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("contents")]
    public List<MCPResourceContentSummary> Contents { get; set; } = new();

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

public sealed class MCPResourceSummary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }
}

public sealed class MCPResourceTemplateSummary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("uriTemplate")]
    public string UriTemplate { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("isTemplated")]
    public bool IsTemplated { get; set; }
}

public sealed class MCPResourceContentSummary
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    [JsonPropertyName("byteLength")]
    public int? ByteLength { get; set; }
}

public sealed class MCPPromptListResult
{
    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("prompts")]
    public List<MCPPromptSummary> Prompts { get; set; } = new();

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

public sealed class MCPPromptGetResult
{
    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("messages")]
    public List<MCPPromptMessageSummary> Messages { get; set; } = new();

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

public sealed class MCPPromptSummary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("arguments")]
    public List<MCPPromptArgumentSummary> Arguments { get; set; } = new();
}

public sealed class MCPPromptArgumentSummary
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class MCPPromptMessageSummary
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public MCPPromptContentSummary Content { get; set; } = new();
}

public sealed class MCPPromptContentSummary
{
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("uri")]
    public string? Uri { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    [JsonPropertyName("byteLength")]
    public int? ByteLength { get; set; }
}
