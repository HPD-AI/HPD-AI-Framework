using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Environment.Contracts;

namespace HPD.Agent.MCP;

/// <summary>Contains the single authoritative MCP server manifest.</summary>
public sealed class McpManifest
{
    /// <summary>Gets or sets uniquely named MCP server registrations.</summary>
    [JsonPropertyName("servers")]
    public List<McpServerConfig> Servers { get; set; } = [];

    /// <summary>Validates uniqueness and every enabled registration.</summary>
    public void Validate()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var server in Servers)
        {
            server.Validate();
            if (!names.Add(server.Name))
                throw new ArgumentException($"MCP server name '{server.Name}' is registered more than once.");
        }
    }
}

/// <summary>Configures one MCP server registration without claiming negotiated runtime facts.</summary>
public sealed class McpServerConfig
{
    /// <summary>Gets or sets the stable registration name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    /// <summary>Gets or sets the transport kind: <c>stdio</c> or <c>http</c>.</summary>
    [JsonPropertyName("transport")]
    public string Transport { get; set; } = string.Empty;
    /// <summary>Gets or sets the stdio executable.</summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }
    /// <summary>Gets or sets stdio arguments.</summary>
    [JsonPropertyName("arguments")]
    public List<string> Arguments { get; set; } = [];
    /// <summary>Gets or sets the stdio working directory.</summary>
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }
    /// <summary>Gets or sets whether the process inherits the host environment.</summary>
    [JsonPropertyName("inheritEnvironmentVariables")]
    public bool InheritEnvironmentVariables { get; set; } = true;
    /// <summary>Gets or sets whether HPD's default process environment is applied.</summary>
    [JsonPropertyName("useDefaultEnvironmentVariables")]
    public bool UseDefaultEnvironmentVariables { get; set; }
    /// <summary>Gets or sets literal environment overrides.</summary>
    [JsonPropertyName("environment")]
    public Dictionary<string, string?>? Environment { get; set; }
    /// <summary>Gets or sets environment-variable to secret-key mappings.</summary>
    [JsonPropertyName("environmentSecretKeys")]
    public Dictionary<string, string>? EnvironmentSecretKeys { get; set; }
    /// <summary>Gets or sets process-isolation policy.</summary>
    [JsonPropertyName("processIsolation")]
    public McpProcessIsolationOptions? ProcessIsolation { get; set; }
    /// <summary>Gets or sets the Streamable HTTP endpoint.</summary>
    [JsonPropertyName("endpoint")]
    public Uri? Endpoint { get; set; }
    /// <summary>Gets or sets additive non-reserved HTTP headers.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }
    /// <summary>Gets or sets HTTP-header to secret-key mappings.</summary>
    [JsonPropertyName("headerSecretKeys")]
    public Dictionary<string, string>? HeaderSecretKeys { get; set; }
    /// <summary>Gets or sets a server-level exact protocol override.</summary>
    [JsonPropertyName("exactVersion")]
    public string? ExactVersion { get; set; }
    /// <summary>Gets or sets an optional container description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    /// <summary>Gets or sets whether this registration contributes capabilities.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    /// <summary>Gets or sets whether functions appear behind an MCP container.</summary>
    [JsonPropertyName("enableCollapsing")]
    public bool EnableCollapsing { get; set; }
    /// <summary>Gets or sets the default permission requirement.</summary>
    [JsonPropertyName("requiresPermission")]
    public bool RequiresPermission { get; set; }
    /// <summary>Gets or sets whether explicit resource functions are projected.</summary>
    [JsonPropertyName("enableResources")]
    public bool EnableResources { get; set; }
    /// <summary>Gets or sets whether explicit prompt functions are projected.</summary>
    [JsonPropertyName("enablePrompts")]
    public bool EnablePrompts { get; set; }
    /// <summary>Gets or sets invocation mode policy for ordinary local observation.</summary>
    [JsonPropertyName("invocationModePolicy")]
    public AgentInvocationModePolicy InvocationModePolicy { get; set; } =
        AgentInvocationModePolicy.SynchronousOnly;
    /// <summary>Gets or sets exact original-tool invocation policy overrides.</summary>
    [JsonPropertyName("toolInvocationModePolicies")]
    public Dictionary<string, AgentInvocationModePolicy> ToolInvocationModePolicies { get; set; } =
        new(StringComparer.Ordinal);
    /// <summary>Gets or sets the maximum number of resources returned by one projected list call.</summary>
    [JsonPropertyName("maxResourceListResults")]
    public int MaxResourceListResults { get; set; } = 100;
    /// <summary>Gets or sets the maximum text length returned by one projected resource read.</summary>
    [JsonPropertyName("maxResourceContentLength")]
    public int MaxResourceContentLength { get; set; } = 200_000;
    /// <summary>Gets or sets the maximum number of prompts returned by one projected list call.</summary>
    [JsonPropertyName("maxPromptListResults")]
    public int MaxPromptListResults { get; set; } = 100;
    /// <summary>Gets or sets the maximum text length returned by one projected prompt retrieval.</summary>
    [JsonPropertyName("maxPromptContentLength")]
    public int MaxPromptContentLength { get; set; } = 200_000;
    /// <summary>Gets or sets OAuth registration policy for this HTTP resource.</summary>
    [JsonPropertyName("oauth")]
    public McpOAuthOptions? OAuth { get; set; }

    /// <summary>Gets the generated parent ToolHarness identity.</summary>
    [JsonIgnore]
    public string? ParentToolHarness { get; internal set; }
    /// <summary>Gets whether generated ToolHarness ownership adds a nested MCP container.</summary>
    [JsonIgnore]
    public bool CollapseWithinToolHarness { get; internal set; }

    /// <summary>Validates final configuration and rejects session-era or reserved-header behavior.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        if (!IsStdio && !IsHttp)
            throw new ArgumentException($"Unsupported MCP transport '{Transport}'.");
        if (IsStdio && string.IsNullOrWhiteSpace(Command))
            throw new ArgumentException($"Stdio MCP server '{Name}' requires a command.");
        if (IsHttp && (Endpoint is null || !Endpoint.IsAbsoluteUri ||
            (Endpoint.Scheme != Uri.UriSchemeHttp && Endpoint.Scheme != Uri.UriSchemeHttps)))
            throw new ArgumentException($"HTTP MCP server '{Name}' requires an absolute HTTP(S) endpoint.");
        if (IsHttp && ProcessIsolation is not null)
            throw new ArgumentException("Process isolation is valid only for stdio servers.");
        if (IsStdio && OAuth is not null)
            throw new ArgumentException("OAuth is valid only for HTTP servers.");
        ValidateHeaders(Headers?.Keys);
        ValidateHeaders(HeaderSecretKeys?.Keys);
        ProcessIsolation?.Validate();
        ValidateOAuth();
        if (MaxResourceListResults <= 0 || MaxPromptListResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxResourceListResults), "MCP list limits must be positive.");
        if (MaxResourceContentLength <= 0 || MaxPromptContentLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxResourceContentLength), "MCP content limits must be positive.");
    }

    /// <summary>Gets whether the registration uses stdio.</summary>
    [JsonIgnore]
    public bool IsStdio => string.Equals(Transport, "stdio", StringComparison.OrdinalIgnoreCase);
    /// <summary>Gets whether the registration uses Streamable HTTP.</summary>
    [JsonIgnore]
    public bool IsHttp => string.Equals(Transport, "http", StringComparison.OrdinalIgnoreCase);

    private void ValidateOAuth()
    {
        if (OAuth is null)
            return;
        if (OAuth.RedirectUri is null || !OAuth.RedirectUri.IsAbsoluteUri)
            throw new ArgumentException("OAuth requires an absolute redirect URI.");
        if (OAuth.RegistrationMode == McpOAuthClientRegistrationMode.DynamicRegistration &&
            !OAuth.AllowDynamicRegistration)
            throw new ArgumentException("Dynamic registration requires allowDynamicRegistration=true.");
        if (OAuth.ClientIdMetadataDocument is { } document &&
            (!document.IsAbsoluteUri || document.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Client ID Metadata Documents require an absolute HTTPS URI.");
    }

    private static void ValidateHeaders(IEnumerable<string>? headers)
    {
        if (headers is null)
            return;
        foreach (var header in headers)
        {
            if (ReservedHeaders.Contains(header))
                throw new ArgumentException($"Header '{header}' is reserved by MCP and cannot be overridden.");
        }
    }

    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "MCP-Protocol-Version",
        "MCP-Session-Id",
        "MCP-Method",
        "MCP-Name"
    };
}

/// <summary>Configures enforcement delegated to the application process provider.</summary>
public sealed class McpProcessIsolationOptions
{
    /// <summary>Gets or sets whether the process must use the isolated provider path.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
    /// <summary>Gets or sets whether network access is requested.</summary>
    [JsonPropertyName("allowNetwork")]
    public bool AllowNetwork { get; set; }
    /// <summary>Gets or sets allowed filesystem roots.</summary>
    [JsonPropertyName("allowedPaths")]
    public IReadOnlyList<string> AllowedPaths { get; set; } = [];
    /// <summary>Gets or sets the maximum accepted newline-delimited JSON-RPC message size.</summary>
    [JsonPropertyName("maxMessageBytes")]
    public int MaximumMessageBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Validates bounded isolation inputs without claiming enforcement.</summary>
    internal void Validate()
    {
        if (AllowedPaths.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Process-isolation paths cannot be blank.");
        if (MaximumMessageBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumMessageBytes));
    }

    /// <summary>Builds the exact process-provider policy requested by this final manifest.</summary>
    internal ProcessIsolationPolicy ToPolicy() => ProcessIsolationPolicy.Default with
    {
        Mode = Enabled ? ProcessIsolationMode.Isolated : ProcessIsolationMode.Disabled,
        Filesystem = new FilesystemAccessPolicy
        {
            Rules = AllowedPaths.Select(path => new PathAccessRule
            {
                Kind = PathAccessRuleKind.AllowRead,
                Path = new HostPath(path)
            }).Concat(AllowedPaths.Select(path => new PathAccessRule
            {
                Kind = PathAccessRuleKind.AllowWrite,
                Path = new HostPath(path)
            })).ToArray()
        },
        Network = AllowNetwork
            ? new NetworkEgressPolicy { Mode = NetworkEgressMode.Unrestricted }
            : NetworkEgressPolicy.Blocked
    };
}
