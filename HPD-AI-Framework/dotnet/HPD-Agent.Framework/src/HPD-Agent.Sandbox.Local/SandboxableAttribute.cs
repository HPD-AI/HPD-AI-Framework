namespace HPD.Sandbox.Local;

/// <summary>
/// Marks a function as requiring sandbox execution.
/// Uses comma-separated strings for configuration (C# attribute limitation).
/// </summary>
/// <remarks>
/// <para>Apply to functions that execute shell commands or untrusted code.</para>
/// <para>The SandboxMiddleware will automatically wrap these functions.</para>
/// <para>
/// For source generator integration, this attribute can be combined with
/// [AIFunction] to generate sandbox wrapper code at compile time.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Basic - Default restrictive settings (no network, deny ~/.ssh etc.)
/// [AIFunction]
/// [RequiresPermission]
/// [Sandboxable]
/// public async Task&lt;string&gt; ExecuteCommand(string command)
/// {
///     // Sandboxed execution
/// }
///
/// // With profile
/// [AIFunction]
/// [Sandboxable(Profile = "network-only", AllowedDomains = "api.weather.com")]
/// public async Task&lt;string&gt; GetWeather(string city)
/// {
///     // Can access api.weather.com, but ~/.ssh is still blocked
/// }
///
/// // Full custom configuration
/// [AIFunction]
/// [RequiresPermission]
/// [Sandboxable(
///     AllowedDomains = "api.github.com,*.npmjs.org",
///     DeniedDomains = "malicious.npmjs.org",
///     AllowWrite = "./workspace,./cache,/tmp",
///     DenyRead = "~/.ssh,~/.aws,~/.gnupg,~/.config")]
/// public async Task&lt;string&gt; RunBuildScript(string script)
/// {
///     // Custom sandbox configuration
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SandboxableAttribute : Attribute
{
    /// <summary>
    /// Preset profile: "restrictive", "permissive", "network-only", "filesystem-only"
    /// If set, provides defaults that can be overridden by other properties.
    /// </summary>
    /// <remarks>
    /// <para>Profiles:</para>
    /// <list type="bullet">
    /// <item><b>restrictive</b> (default): No network, deny ~/.ssh, ~/.aws, ~/.gnupg</item>
    /// <item><b>permissive</b>: All network allowed, minimal restrictions</item>
    /// <item><b>network-only</b>: Allow network but deny sensitive paths</item>
    /// <item><b>filesystem-only</b>: No network, allow workspace writes</item>
    /// </list>
    /// </remarks>
    public string Profile { get; set; } = "";

    /// <summary>
    /// Comma-separated allowed domains (empty string = no network access).
    /// Supports wildcards: "*.github.com,api.weather.com"
    /// </summary>
    /// <example>
    /// AllowedDomains = "api.github.com,*.npmjs.org"
    /// </example>
    public string AllowedDomains { get; set; } = "";

    /// <summary>
    /// Comma-separated domains to explicitly deny (takes precedence).
    /// </summary>
    /// <example>
    /// DeniedDomains = "malicious.example.com"
    /// </example>
    public string DeniedDomains { get; set; } = "";

    /// <summary>
    /// Comma-separated paths this function can write to.
    /// Default: ".,/tmp"
    /// </summary>
    /// <example>
    /// AllowWrite = "./workspace,./output,/tmp"
    /// </example>
    public string AllowWrite { get; set; } = ".,/tmp";

    /// <summary>
    /// Comma-separated paths this function cannot read.
    /// Default: "~/.ssh,~/.aws,~/.gnupg"
    /// </summary>
    /// <example>
    /// DenyRead = "~/.ssh,~/.aws,~/.gnupg,~/.config/secrets"
    /// </example>
    public string DenyRead { get; set; } = "~/.ssh,~/.aws,~/.gnupg";

    /// <summary>
    /// Parses AllowedDomains into an array.
    /// </summary>
    public string[] GetAllowedDomains() =>
        string.IsNullOrWhiteSpace(AllowedDomains)
            ? []
            : AllowedDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Parses DeniedDomains into an array.
    /// </summary>
    public string[] GetDeniedDomains() =>
        string.IsNullOrWhiteSpace(DeniedDomains)
            ? []
            : DeniedDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Parses AllowWrite into an array.
    /// </summary>
    public string[] GetAllowWrite() =>
        string.IsNullOrWhiteSpace(AllowWrite)
            ? [".", "/tmp"]
            : AllowWrite.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Parses DenyRead into an array.
    /// </summary>
    public string[] GetDenyRead() =>
        string.IsNullOrWhiteSpace(DenyRead)
            ? ["~/.ssh", "~/.aws", "~/.gnupg"]
            : DenyRead.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
