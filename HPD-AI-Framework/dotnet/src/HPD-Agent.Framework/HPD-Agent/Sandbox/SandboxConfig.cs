namespace HPD.Agent.Sandbox;

/// <summary>
/// Complete sandbox configuration.
/// Immutable after passing to SandboxMiddleware.
/// </summary>
public sealed record SandboxConfig
{
    /// <summary>
    /// Paths where writes are allowed (default: current directory + /tmp).
    /// </summary>
    /// <remarks>
    /// <para>Paths are resolved relative to current working directory.</para>
    /// <para>Special values:</para>
    /// <list type="bullet">
    /// <item>"." - Current directory</item>
    /// <item>"~" - User home directory</item>
    /// <item>"/tmp" - System temp directory</item>
    /// </list>
    /// </remarks>
    public string[] AllowWrite { get; init; } = [".", "/tmp"];

    /// <summary>
    /// Paths that cannot be read (overrides read-only root access).
    /// </summary>
    /// <remarks>
    /// <para>Use this to deny access to sensitive directories.</para>
    /// <para>Common patterns: ~/.ssh, ~/.aws, ~/.gnupg, ~/.config</para>
    /// </remarks>
    public string[] DenyRead { get; init; } =
    [
        "~/.ssh",
        "~/.aws",
        "~/.gnupg"
    ];

    /// <summary>
    /// Paths that cannot be written even if they're under AllowWrite paths.
    /// </summary>
    /// <remarks>
    /// <para>Use this to protect specific files within writable directories.</para>
    /// <para>Enhanced sandbox auto-discovers dangerous files; use this for additional protection.</para>
    /// </remarks>
    public string[] DenyWrite { get; init; } = [];

    /// <summary>
    /// Domains allowed for network access (empty = no network).
    /// </summary>
    /// <remarks>
    /// <para>Supports wildcards: "*.github.com" matches "api.github.com"</para>
    /// <para>If empty array, all network access is blocked.</para>
    /// <para>If null, network filtering is disabled (all domains allowed).</para>
    /// </remarks>
    public string[]? AllowedDomains { get; init; } = [];

    /// <summary>
    /// Domains to explicitly deny (takes precedence over AllowedDomains).
    /// </summary>
    /// <remarks>
    /// <para>Use this to block specific subdomains when using wildcards.</para>
    /// <para>Example: Allow "*.github.com" but deny "malicious.github.com"</para>
    /// </remarks>
    public string[] DeniedDomains { get; init; } = [];

    /// <summary>
    /// Function names that should always be sandboxed.
    /// </summary>
    /// <remarks>
    /// <para>In addition to auto-detection, explicitly list functions here.</para>
    /// <para>Supports wildcards: "MCP*" matches all MCP functions.</para>
    /// </remarks>
    public string[] SandboxableFunctions { get; init; } = [];

    /// <summary>
    /// Function names that should never be sandboxed.
    /// </summary>
    /// <remarks>
    /// <para>Excludes functions from sandboxing even if they match other criteria.</para>
    /// <para>Use for trusted internal functions.</para>
    /// </remarks>
    public string[] ExcludedFunctions { get; init; } = [];

    /// <summary>
    /// Behavior when sandbox initialization fails.
    /// </summary>
    /// <remarks>
    /// <para><c>Block</c> (default): Prevent function execution, emit error event.</para>
    /// <para><c>Warn</c>: Log warning, allow unsandboxed execution.</para>
    /// <para><c>Ignore</c>: Silently allow unsandboxed execution.</para>
    /// </remarks>
    public SandboxFailureBehavior OnInitializationFailure { get; init; } = SandboxFailureBehavior.Block;

    /// <summary>
    /// Behavior when a sandbox violation is detected.
    /// </summary>
    /// <remarks>
    /// <para><c>EmitEvent</c> (default): Emit <c>SandboxViolationEvent</c>, continue.</para>
    /// <para><c>BlockAndEmit</c>: Emit event and block subsequent calls from same function.</para>
    /// <para><c>Ignore</c>: Silently continue.</para>
    /// </remarks>
    public SandboxViolationBehavior OnViolation { get; init; } = SandboxViolationBehavior.EmitEvent;

    /// <summary>
    /// Enable weaker sandbox for Docker containers (Linux only).
    /// </summary>
    /// <remarks>
    /// <para>Significantly weakens security.</para>
    /// <para>Only use when running inside Docker without privileged namespaces.</para>
    /// </remarks>
    public bool EnableWeakerNestedSandbox { get; init; } = false;

    /// <summary>
    /// Enable real-time violation monitoring (macOS only).
    /// </summary>
    /// <remarks>
    /// <para>Spawns background 'log stream' process (~5MB RAM).</para>
    /// <para>On Linux, this setting is ignored.</para>
    /// </remarks>
    public bool EnableViolationMonitoring { get; init; } = false;

    // ============================================================
    // Enhanced Sandbox Settings (now always enabled)
    // ============================================================

    /// <summary>
    /// Maximum directory depth to scan for dangerous files.
    /// </summary>
    /// <remarks>
    /// <para>Higher values provide more protection but slower initialization.</para>
    /// <para>Default: 3 (scans current dir + up to 3 levels deep).</para>
    /// </remarks>
    public int MandatoryDenySearchDepth { get; init; } = 3;

    /// <summary>
    /// Allow write access to git config files (.gitconfig, .git/config).
    /// </summary>
    /// <remarks>
    /// <para>Warning: Enabling this can allow sandbox escape via git hooks.</para>
    /// <para>Default: false.</para>
    /// </remarks>
    public bool AllowGitConfig { get; init; } = false;

    /// <summary>
    /// Allow creation of Unix domain sockets (Linux only).
    /// </summary>
    /// <remarks>
    /// <para>When false, seccomp blocks socket(AF_UNIX, ...) syscalls.</para>
    /// <para>Warning: Unix sockets can potentially bypass network isolation.</para>
    /// <para>Default: false.</para>
    /// </remarks>
    public bool AllowAllUnixSockets { get; init; } = false;

    /// <summary>
    /// Specific Unix socket paths to allow (macOS only).
    /// </summary>
    /// <remarks>
    /// <para>More granular than AllowAllUnixSockets - allows specific sockets only.</para>
    /// <para>Common use cases:</para>
    /// <list type="bullet">
    /// <item>/var/run/docker.sock - Docker daemon</item>
    /// <item>~/.ssh/agent.sock - SSH agent</item>
    /// </list>
    /// <para>If null or empty, no specific sockets are allowed (unless AllowAllUnixSockets is true).</para>
    /// </remarks>
    public string[]? AllowUnixSockets { get; init; } = null;

    /// <summary>
    /// Allow pseudo-terminal (PTY) access (macOS only).
    /// </summary>
    /// <remarks>
    /// <para>Required for interactive terminal applications.</para>
    /// <para>Default: false.</para>
    /// </remarks>
    public bool AllowPty { get; init; } = false;

    /// <summary>
    /// Allow binding to local network interfaces (macOS only).
    /// </summary>
    /// <remarks>
    /// <para>Required for local server applications.</para>
    /// <para>Default: false.</para>
    /// </remarks>
    public bool AllowLocalBinding { get; init; } = false;

    /// <summary>
    /// Regex patterns for sandbox violations to ignore.
    /// </summary>
    /// <remarks>
    /// <para>Use to suppress expected/benign violations.</para>
    /// <para>Example: ["file-read-data.*\\.cache"]</para>
    /// </remarks>
    public string[]? IgnoreViolationPatterns { get; init; } = null;

    /// <summary>
    /// Environment variables to pass through to sandboxed processes.
    /// </summary>
    /// <remarks>
    /// <para>By default, only safe variables are passed (PATH, HOME, TERM).</para>
    /// <para>Add variables here to allow them through.</para>
    /// <para>Never include sensitive variables (API keys, tokens).</para>
    /// </remarks>
    public string[] AllowedEnvironmentVariables { get; init; } = ["PATH", "HOME", "TERM", "LANG"];

    // ============================================================
    // External Proxy Settings (for enterprise environments)
    // ============================================================

    /// <summary>
    /// Use an external HTTP proxy instead of starting one.
    /// </summary>
    /// <remarks>
    /// <para>When set, the sandbox uses an existing HTTP proxy on this port.</para>
    /// <para>Useful in enterprise environments with existing proxy infrastructure.</para>
    /// <para>If null (default), sandbox starts its own HTTP proxy.</para>
    /// </remarks>
    public int? ExternalHttpProxyPort { get; init; } = null;

    /// <summary>
    /// Use an external SOCKS5 proxy instead of starting one.
    /// </summary>
    /// <remarks>
    /// <para>When set, the sandbox uses an existing SOCKS5 proxy on this port.</para>
    /// <para>SOCKS5 proxy is used on Linux for network isolation within bwrap namespaces.</para>
    /// <para>If null (default), sandbox starts its own SOCKS5 proxy.</para>
    /// </remarks>
    public int? ExternalSocksProxyPort { get; init; } = null;

    /// <summary>
    /// Creates a restrictive default configuration.
    /// </summary>
    public static SandboxConfig CreateDefault() => new();

    /// <summary>
    /// Creates a permissive configuration (allows network, minimal restrictions).
    /// </summary>
    public static SandboxConfig CreatePermissive() => new()
    {
        DenyRead = [],
        AllowedDomains = null  // null = no filtering
    };

    /// <summary>
    /// Creates a configuration optimized for MCP servers.
    /// </summary>
    public static SandboxConfig CreateForMCP() => new()
    {
        AllowedDomains =
        [
            "*.npmjs.org",
            "*.pypi.org",
            "registry.yarnpkg.com"
        ],
        AllowWrite = [".", "/tmp"],
        DenyRead = ["~/.ssh", "~/.aws", "~/.gnupg", "~/.config"],
        SandboxableFunctions = ["MCP*", "*Server*"],
        EnableViolationMonitoring = true
    };

    /// <summary>
    /// Creates a configuration optimized for maximum security.
    /// </summary>
    /// <remarks>
    /// <para>Uses enhanced sandbox features:</para>
    /// <list type="bullet">
    /// <item>Automatic dangerous file protection</item>
    /// <item>Seccomp filtering (Linux)</item>
    /// <item>Stricter Seatbelt profiles (macOS)</item>
    /// <item>Unix socket blocking</item>
    /// </list>
    /// </remarks>
    public static SandboxConfig CreateEnhanced() => new()
    {
        AllowWrite = [".", "/tmp"],
        DenyRead = ["~/.ssh", "~/.aws", "~/.gnupg", "~/.config"],
        AllowedDomains = [],
        MandatoryDenySearchDepth = 3,
        AllowGitConfig = false,
        AllowAllUnixSockets = false,
        AllowPty = false,
        AllowLocalBinding = false,
        EnableViolationMonitoring = true
    };

    /// <summary>
    /// Creates a configuration optimized for MCP servers with maximum security.
    /// </summary>
    public static SandboxConfig CreateEnhancedForMCP() => new()
    {
        AllowedDomains =
        [
            "*.npmjs.org",
            "*.pypi.org",
            "registry.yarnpkg.com"
        ],
        AllowWrite = [".", "/tmp"],
        DenyRead = ["~/.ssh", "~/.aws", "~/.gnupg", "~/.config"],
        SandboxableFunctions = ["MCP*", "*Server*"],
        MandatoryDenySearchDepth = 3,
        AllowGitConfig = false,
        AllowAllUnixSockets = false,
        EnableViolationMonitoring = true
    };

    /// <summary>
    /// Validates configuration for correctness.
    /// </summary>
    /// <exception cref="ArgumentException">If configuration is invalid.</exception>
    public void Validate()
    {
        if (AllowWrite.Length == 0)
            throw new ArgumentException("At least one writable path must be specified.");

        foreach (var path in AllowWrite.Concat(DenyRead).Concat(DenyWrite))
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Paths cannot be empty or whitespace.");
        }

        if (AllowedDomains != null)
        {
            foreach (var domain in AllowedDomains)
            {
                if (string.IsNullOrWhiteSpace(domain))
                    throw new ArgumentException("Domain patterns cannot be empty.");
            }
        }

        // Enhanced sandbox validation
        if (MandatoryDenySearchDepth < 0)
            throw new ArgumentException("MandatoryDenySearchDepth must be non-negative.");

        if (MandatoryDenySearchDepth > 10)
            throw new ArgumentException("MandatoryDenySearchDepth cannot exceed 10 (performance protection).");

        if (IgnoreViolationPatterns != null)
        {
            foreach (var pattern in IgnoreViolationPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    throw new ArgumentException("Violation patterns cannot be empty.");

                // Validate regex syntax
                try
                {
                    _ = new System.Text.RegularExpressions.Regex(pattern);
                }
                catch (System.Text.RegularExpressions.RegexParseException ex)
                {
                    throw new ArgumentException($"Invalid regex pattern '{pattern}': {ex.Message}");
                }
            }
        }
    }
}

/// <summary>
/// Behavior when sandbox initialization fails.
/// </summary>
public enum SandboxFailureBehavior
{
    /// <summary>Block function execution and emit error event.</summary>
    Block,
    /// <summary>Log warning and allow unsandboxed execution.</summary>
    Warn,
    /// <summary>Silently allow unsandboxed execution.</summary>
    Ignore
}

/// <summary>
/// Behavior when a sandbox violation is detected.
/// </summary>
public enum SandboxViolationBehavior
{
    /// <summary>Emit event and continue execution.</summary>
    EmitEvent,
    /// <summary>Emit event and block subsequent calls from violating function.</summary>
    BlockAndEmit,
    /// <summary>Silently continue.</summary>
    Ignore
}
