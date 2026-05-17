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
    /// Paths that should be re-allowed for reading within denied regions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this to deny a broad region and allow back specific paths.
    /// For example, deny <c>~</c> but allow <c>~/work/project</c>.
    /// </para>
    /// <para>
    /// This is required for parity with local sandbox runtimes that model
    /// reads as "deny, then allow within deny."
    /// </para>
    /// </remarks>
    public string[] AllowRead { get; init; } = [];

    /// <summary>
    /// Paths that cannot be written even if they're under AllowWrite paths.
    /// </summary>
    /// <remarks>
    /// <para>Use this to protect specific files within writable directories.</para>
    /// <para>Enhanced sandbox auto-discovers dangerous files; use this for additional protection.</para>
    /// </remarks>
    public string[] DenyWrite { get; init; } = [];

    /// <summary>
    /// Explicit network mode.
    /// </summary>
    public SandboxNetworkMode NetworkMode { get; init; } = SandboxNetworkMode.Blocked;

    /// <summary>
    /// Domains allowed for network access.
    /// </summary>
    /// <remarks>
    /// <para>Supports wildcards: "*.github.com" matches subdomains such as "api.github.com".</para>
    /// <para>This list is used only when <see cref="NetworkMode"/> is <see cref="SandboxNetworkMode.Filtered"/>.</para>
    /// </remarks>
    public string[] AllowedDomains { get; init; } = [];

    /// <summary>
    /// Domains to explicitly deny (takes precedence over AllowedDomains).
    /// </summary>
    /// <remarks>
    /// <para>Use this to block specific subdomains when using wildcards.</para>
    /// <para>Example: Allow "*.github.com" but deny "malicious.github.com"</para>
    /// </remarks>
    public string[] DeniedDomains { get; init; } = [];

    public bool IsNetworkFiltered => NetworkMode == SandboxNetworkMode.Filtered;

    public bool IsNetworkBlocked => NetworkMode == SandboxNetworkMode.Blocked;

    public bool IsNetworkUnrestricted => NetworkMode == SandboxNetworkMode.Unrestricted;

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
    /// Optional absolute path to a Linux seccomp helper binary.
    /// </summary>
    /// <remarks>
    /// <para>When omitted, the local sandbox prefers the packaged runtime helper for the current Linux architecture.</para>
    /// </remarks>
    public string? SeccompHelperPath { get; init; } = null;

    /// <summary>
    /// Allow building the Linux seccomp helper at runtime when no packaged or explicit helper is available.
    /// </summary>
    /// <remarks>
    /// <para>Default: false. Package and CI builds should provide native helpers instead of compiling during sandbox initialization.</para>
    /// </remarks>
    public bool AllowSeccompRuntimeCompilation { get; init; } = false;

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
    /// Allow lookup of macOS trustd from sandboxed processes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some TLS stacks consult trustd even when proxy-based network filtering is
    /// enabled. Allowing this service improves compatibility but weakens the
    /// isolation surface, so it is opt-in.
    /// </para>
    /// <para>Default: false.</para>
    /// </remarks>
    public bool AllowMacOSTrustdLookup { get; init; } = false;

    /// <summary>
    /// Additional macOS Mach services to allow lookup for.
    /// </summary>
    /// <remarks>
    /// <para>Supports exact service names and one trailing wildcard, for example <c>com.example.service</c> or <c>com.example.*</c>.</para>
    /// <para><c>*</c> allows all Mach lookups and should be used only for intentionally weaker isolation.</para>
    /// </remarks>
    public string[] AllowMachLookup { get; init; } = [];

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
    /// Optional upstream proxy configuration for sandbox-owned HTTP/SOCKS proxies.
    /// </summary>
    /// <remarks>
    /// <para>Explicit values take precedence over HTTP_PROXY, HTTPS_PROXY, and NO_PROXY.</para>
    /// <para>Schemeless values such as <c>proxy.corp:8080</c> are treated as HTTP proxies.</para>
    /// </remarks>
    public ParentProxyConfig? ParentProxy { get; init; } = null;

    /// <summary>
    /// Optional in-process TLS termination settings for HTTPS request filtering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When enabled without explicit CA paths, the sandbox runtime will generate
    /// an ephemeral CA for the session and expose trust environment variables to
    /// sandboxed processes.
    /// </para>
    /// <para>Cannot be combined with <see cref="MitmProxy"/>.</para>
    /// </remarks>
    public TlsTerminationConfig? TlsTermination { get; init; } = null;

    /// <summary>
    /// Optional external MITM proxy used for CONNECT interception.
    /// </summary>
    /// <remarks>
    /// <para>Cannot be combined with <see cref="TlsTermination"/> because both features own CONNECT interception semantics.</para>
    /// </remarks>
    public MitmProxyConfig? MitmProxy { get; init; } = null;

    /// <summary>
    /// Optional request-level policy callback for plain HTTP requests.
    /// </summary>
    /// <remarks>
    /// <para>The callback runs after domain policy allows a request and before upstream forwarding.</para>
    /// <para>If the callback throws, the proxy fails closed and denies the request.</para>
    /// <para>HTTPS request details require TLS termination and are not visible for CONNECT tunnels yet.</para>
    /// </remarks>
    public SandboxRequestFilter? RequestFilter { get; init; } = null;

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
        NetworkMode = SandboxNetworkMode.Unrestricted,
    };

    /// <summary>
    /// Creates a configuration optimized for MCP servers.
    /// </summary>
    public static SandboxConfig CreateForMCP() => new()
    {
        NetworkMode = SandboxNetworkMode.Filtered,
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
        NetworkMode = SandboxNetworkMode.Blocked,
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
        NetworkMode = SandboxNetworkMode.Filtered,
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

        foreach (var path in AllowWrite.Concat(DenyRead).Concat(AllowRead).Concat(DenyWrite))
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Paths cannot be empty or whitespace.");
        }

        if (NetworkMode == SandboxNetworkMode.Filtered && AllowedDomains is not { Length: > 0 })
            throw new ArgumentException("AllowedDomains must contain at least one domain when NetworkMode is Filtered.");

        if (NetworkMode is SandboxNetworkMode.Blocked or SandboxNetworkMode.Unrestricted &&
            DeniedDomains.Length > 0)
            throw new ArgumentException("DeniedDomains can only be used when network mode is Filtered.");

        foreach (var pattern in AllowMachLookup)
            ValidateMachLookupPattern(pattern);

        foreach (var domain in AllowedDomains)
        {
            if (string.IsNullOrWhiteSpace(domain))
                throw new ArgumentException("Domain patterns cannot be empty.");
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

        ParentProxy?.Validate();
        TlsTermination?.Validate();
        MitmProxy?.Validate();

        if (TlsTermination is not null && MitmProxy is not null)
            throw new ArgumentException("TlsTermination and MitmProxy cannot both be configured.");

        if (!string.IsNullOrWhiteSpace(SeccompHelperPath) && !Path.IsPathFullyQualified(SeccompHelperPath))
            throw new ArgumentException("SeccompHelperPath must be an absolute path.");
    }

    private static void ValidateMachLookupPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Mach lookup patterns cannot be empty.");

        if (pattern == "*")
            return;

        if (pattern.Any(char.IsControl))
            throw new ArgumentException($"Invalid Mach lookup pattern '{pattern}'.");

        var wildcardIndex = pattern.IndexOf('*');
        if (wildcardIndex < 0)
            return;

        if (!pattern.EndsWith(".*", StringComparison.Ordinal) ||
            wildcardIndex != pattern.Length - 1 ||
            pattern.Length <= 2)
        {
            throw new ArgumentException(
                $"Invalid Mach lookup pattern '{pattern}'. Only exact names, '*', and one trailing wildcard like 'com.example.*' are supported.");
        }
    }
}

/// <summary>
/// Explicit network mode for local sandboxing.
/// </summary>
public enum SandboxNetworkMode
{
    /// <summary>No network egress is allowed.</summary>
    Blocked,

    /// <summary>Network egress is allowed only through sandbox proxies and domain policy.</summary>
    Filtered,

    /// <summary>Network filtering is disabled.</summary>
    Unrestricted,
}

/// <summary>
/// Upstream proxy settings used by local sandbox proxy servers.
/// </summary>
public sealed record ParentProxyConfig
{
    /// <summary>Proxy used for plain HTTP destinations.</summary>
    public string? HttpProxy { get; init; }

    /// <summary>Proxy used for HTTPS destinations.</summary>
    public string? HttpsProxy { get; init; }

    /// <summary>Comma-separated bypass list using NO_PROXY semantics.</summary>
    public string? NoProxy { get; init; }

    public void Validate()
    {
        ValidateProxyValue(HttpProxy, nameof(HttpProxy));
        ValidateProxyValue(HttpsProxy, nameof(HttpsProxy));
    }

    private static void ValidateProxyValue(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var candidate = value.Contains("://", StringComparison.Ordinal)
            ? value
            : $"http://{value}";

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port <= 0)
        {
            throw new ArgumentException(
                $"{propertyName} must be an absolute http/https proxy URI or schemeless host:port value.");
        }
    }
}

/// <summary>
/// In-process TLS termination settings for sandbox HTTPS request filtering.
/// </summary>
public sealed record TlsTerminationConfig
{
    /// <summary>Optional PEM-encoded CA certificate path.</summary>
    public string? CaCertificatePath { get; init; }

    /// <summary>Optional PEM-encoded CA private key path.</summary>
    public string? CaPrivateKeyPath { get; init; }

    /// <summary>Optional directory for generated leaf certificates.</summary>
    public string? LeafCertificateCacheDirectory { get; init; }

    /// <summary>Whether sandbox trust environment variables should be injected.</summary>
    public bool InjectTrustEnvironmentVariables { get; init; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CaCertificatePath) != string.IsNullOrWhiteSpace(CaPrivateKeyPath))
            throw new ArgumentException("CaCertificatePath and CaPrivateKeyPath must be provided together.");

        ValidateAbsolutePath(CaCertificatePath, nameof(CaCertificatePath));
        ValidateAbsolutePath(CaPrivateKeyPath, nameof(CaPrivateKeyPath));
        ValidateAbsolutePath(LeafCertificateCacheDirectory, nameof(LeafCertificateCacheDirectory));
    }

    private static void ValidateAbsolutePath(string? path, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException($"{propertyName} must be an absolute path.");
    }
}

/// <summary>
/// External MITM proxy settings for sandbox CONNECT interception.
/// </summary>
public sealed record MitmProxyConfig
{
    /// <summary>Absolute Unix socket path for the external MITM proxy.</summary>
    public string? UnixSocketPath { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(UnixSocketPath))
            throw new ArgumentException("UnixSocketPath is required for MitmProxy.");

        if (!Path.IsPathFullyQualified(UnixSocketPath))
            throw new ArgumentException("UnixSocketPath must be an absolute path.");
    }
}

/// <summary>
/// Request-level filter callback for sandbox HTTP proxy traffic.
/// </summary>
public delegate ValueTask<SandboxRequestDecision> SandboxRequestFilter(
    SandboxHttpRequest request,
    CancellationToken cancellationToken);

/// <summary>
/// Plain HTTP request metadata visible to sandbox request filters.
/// </summary>
public sealed record SandboxHttpRequest
{
    public required string Method { get; init; }

    public required Uri Uri { get; init; }

    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Decision returned by a sandbox request filter.
/// </summary>
public sealed record SandboxRequestDecision
{
    public required SandboxRequestAction Action { get; init; }

    public string? Reason { get; init; }

    public static SandboxRequestDecision Allow { get; } = new()
    {
        Action = SandboxRequestAction.Allow,
    };

    public static SandboxRequestDecision Deny(string? reason = null) => new()
    {
        Action = SandboxRequestAction.Deny,
        Reason = reason,
    };
}

public enum SandboxRequestAction
{
    Allow,
    Deny,
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
