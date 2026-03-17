using System.Text.Json.Serialization;
using HPD.Agent.Sandbox;

namespace HPD.Agent.MCP;

/// <summary>
/// Sandbox configuration for an MCP server.
/// </summary>
public class MCPSandboxConfig
{
    /// <summary>
    /// Enable/disable sandboxing for this server.
    /// Default: true
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    // Track if AllowedDomains was explicitly set vs defaulted
    private bool _allowedDomainsExplicitlySet = false;
    private string[]? _allowedDomains = [];

    /// <summary>
    /// Domains this server can access (empty = no network).
    /// Supports wildcards: "*.github.com" matches "api.github.com"
    /// </summary>
    /// <remarks>
    /// <para>Default: empty array (no network access)</para>
    /// <para>Set to null to allow all network access (not recommended)</para>
    /// </remarks>
    [JsonPropertyName("allowedDomains")]
    public string[]? AllowedDomains
    {
        get => _allowedDomains;
        set
        {
            _allowedDomains = value;
            _allowedDomainsExplicitlySet = true;
        }
    }

    /// <summary>
    /// Domains to explicitly deny (takes precedence over AllowedDomains).
    /// </summary>
    [JsonPropertyName("deniedDomains")]
    public string[]? DeniedDomains { get; set; }

    /// <summary>
    /// Paths this server can write to.
    /// Default: current directory + /tmp
    /// </summary>
    [JsonPropertyName("allowWrite")]
    public string[]? AllowWrite { get; set; }

    /// <summary>
    /// Paths this server cannot read.
    /// Default: ~/.ssh, ~/.aws, ~/.gnupg
    /// </summary>
    [JsonPropertyName("denyRead")]
    public string[]? DenyRead { get; set; }

    /// <summary>
    /// Use a preset profile instead of manual configuration.
    /// Profiles: "restrictive", "network-only", "filesystem-only", "permissive"
    /// </summary>
    /// <remarks>
    /// <para>When set, other properties are ignored unless they override the profile.</para>
    /// </remarks>
    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    /// <summary>
    /// Paths to explicitly deny writes within allowed paths.
    /// Takes precedence over AllowWrite.
    /// </summary>
    [JsonPropertyName("denyWrite")]
    public string[]? DenyWrite { get; set; }

    /// <summary>
    /// Unix socket paths that are allowed (for Docker, SSH agent, etc.).
    /// macOS: specific paths allowed
    /// Linux: use AllowAllUnixSockets for broader access
    /// </summary>
    [JsonPropertyName("allowUnixSockets")]
    public string[]? AllowUnixSockets { get; set; }

    /// <summary>
    /// Allow ALL Unix sockets (Linux only).
    /// Use this for Docker environments that need broad socket access.
    /// </summary>
    [JsonPropertyName("allowAllUnixSockets")]
    public bool AllowAllUnixSockets { get; set; } = false;

    /// <summary>
    /// Allow binding to local ports (for servers that need to listen).
    /// Default: false
    /// </summary>
    [JsonPropertyName("allowLocalBinding")]
    public bool AllowLocalBinding { get; set; } = false;

    /// <summary>
    /// Enable real-time violation monitoring (macOS only).
    /// </summary>
    [JsonPropertyName("enableViolationMonitoring")]
    public bool EnableViolationMonitoring { get; set; } = false;

    /// <summary>
    /// Map of command patterns to filesystem paths to ignore violations for.
    /// Reduces noise from expected violations.
    /// Use "*" to match all commands.
    /// </summary>
    [JsonPropertyName("ignoreViolations")]
    public Dictionary<string, string[]>? IgnoreViolations { get; set; }

    /// <summary>
    /// Converts to internal SandboxConfig used by platform implementations.
    /// </summary>
    public SandboxConfig ToSandboxConfig()
    {
        // Apply profile defaults if specified
        var baseConfig = Profile?.ToLowerInvariant() switch
        {
            "restrictive" => SandboxConfig.CreateDefault(),
            "permissive" => SandboxConfig.CreatePermissive(),
            "network-only" => new SandboxConfig
            {
                AllowedDomains = null, // Allow all
                AllowWrite = [".", "/tmp"],
                DenyRead = ["~/.ssh", "~/.aws", "~/.gnupg"]
            },
            "filesystem-only" => new SandboxConfig
            {
                AllowedDomains = [], // No network
                AllowWrite = [".", "/tmp"],
                DenyRead = []
            },
            _ => SandboxConfig.CreateDefault()
        };

        // Override with explicit settings only if they differ from defaults
        // AllowedDomains defaults to [], so only override if explicitly set to something else or null
        var effectiveAllowedDomains = _allowedDomainsExplicitlySet
            ? AllowedDomains
            : baseConfig.AllowedDomains;

        return baseConfig with
        {
            AllowedDomains = effectiveAllowedDomains,
            DeniedDomains = DeniedDomains ?? baseConfig.DeniedDomains,
            AllowWrite = AllowWrite ?? baseConfig.AllowWrite,
            DenyRead = DenyRead ?? baseConfig.DenyRead,
            EnableViolationMonitoring = EnableViolationMonitoring
        };
    }
}
