namespace HPD.Agent.Sandbox;

/// <summary>
/// Marks a function as carrying sandbox policy metadata.
/// Uses comma-separated strings for configuration (C# attribute limitation).
/// </summary>
/// <remarks>
/// <para>Apply to functions or tools that execute local processes or untrusted code.</para>
/// <para>
/// The source generator emits this policy into the generated function metadata.
/// During execution, <c>FunctionExecutionContext.SandboxConfigOverride</c>
/// exposes that function-level policy so process-capable tools can pass it to
/// <c>ISandboxedProcessRunner</c>.
/// </para>
/// <para>
/// This attribute is policy metadata. It is not the execution boundary. Tools
/// that start processes must execute through <c>ISandboxedProcessRunner</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Basic marker - inherits the sandbox policy configured by .WithSandbox(...).
/// [AIFunction]
/// [RequiresPermission]
/// [Sandboxable]
/// public async Task&lt;object&gt; ExecuteCommand(
///     string command,
///     FunctionExecutionContext context,
///     CancellationToken cancellationToken)
/// {
///     var runner = context.GetSandboxedProcessRunner();
///     var processCommand = ShellSandboxCommands.PlatformDefault(command);
///     var result = await runner.RunAsync(
///         processCommand,
///         context.SandboxConfigOverride,
///         cancellationToken: cancellationToken);
///     return result;
/// }
///
/// // With function-level policy override
/// [AIFunction]
/// [Sandboxable(NetworkMode = SandboxNetworkPolicy.Filtered, AllowedDomains = "api.weather.com")]
/// public async Task&lt;string&gt; GetWeather(string city, FunctionExecutionContext context)
/// {
///     // Processes started through ISandboxedProcessRunner can access
///     // api.weather.com in addition to the host sandbox policy.
/// }
///
/// // Full sparse override
/// [AIFunction]
/// [RequiresPermission]
/// [Sandboxable(
///     NetworkMode = SandboxNetworkPolicy.Filtered,
///     AllowedDomains = "api.github.com,*.npmjs.org",
///     DeniedDomains = "malicious.npmjs.org",
///     AllowWrite = "./workspace,./cache,/tmp",
///     DenyRead = "~/.ssh,~/.aws,~/.gnupg,~/.config")]
/// public async Task&lt;string&gt; RunBuildScript(string script, FunctionExecutionContext context)
/// {
///     // Custom sandbox overrides are available via context.SandboxConfigOverride.
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SandboxableAttribute : Attribute
{
    /// <summary>
    /// Optional preset profile name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Profiles are host-defined metadata. A bare <see cref="SandboxableAttribute"/>
    /// does not apply a profile or default policy by itself.
    /// </para>
    /// </remarks>
    public string Profile { get; set; } = "";

    /// <summary>
    /// Function-level network override. Inherit means use the runtime policy.
    /// </summary>
    public SandboxNetworkPolicy NetworkMode { get; set; } = SandboxNetworkPolicy.Inherit;

    /// <summary>
    /// Comma-separated allowed domains used when NetworkMode is Filtered.
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
    /// Empty means inherit the runtime writable path policy.
    /// </summary>
    /// <example>
    /// AllowWrite = "./workspace,./output,/tmp"
    /// </example>
    public string AllowWrite { get; set; } = "";

    /// <summary>
    /// Comma-separated paths this function cannot read.
    /// Empty means inherit the runtime denied read policy.
    /// </summary>
    /// <example>
    /// DenyRead = "~/.ssh,~/.aws,~/.gnupg,~/.config/secrets"
    /// </example>
    public string DenyRead { get; set; } = "";

    /// <summary>
    /// Comma-separated paths this function can read back within denied regions.
    /// </summary>
    public string AllowRead { get; set; } = "";

    /// <summary>
    /// Comma-separated paths this function cannot write, even under writable roots.
    /// </summary>
    public string DenyWrite { get; set; } = "";

    /// <summary>
    /// Comma-separated macOS Unix socket paths this function may access.
    /// </summary>
    public string AllowUnixSockets { get; set; } = "";

    /// <summary>
    /// Comma-separated macOS Mach lookup patterns this function may use.
    /// </summary>
    public string AllowMachLookup { get; set; } = "";

    /// <summary>
    /// Allows pseudo-terminal access for this function.
    /// </summary>
    public SandboxToggle AllowPty { get; set; } = SandboxToggle.Inherit;

    /// <summary>
    /// Allows binding local network interfaces for this function.
    /// </summary>
    public SandboxToggle AllowLocalBinding { get; set; } = SandboxToggle.Inherit;

    /// <summary>
    /// Allows all Unix sockets for this function.
    /// </summary>
    public SandboxToggle AllowAllUnixSockets { get; set; } = SandboxToggle.Inherit;

    /// <summary>
    /// Allows macOS trustd lookup for this function.
    /// </summary>
    public SandboxToggle AllowMacOSTrustdLookup { get; set; } = SandboxToggle.Inherit;

    /// <summary>
    /// Allows writes to git configuration files for this function.
    /// </summary>
    public SandboxToggle AllowGitConfig { get; set; } = SandboxToggle.Inherit;

    /// <summary>
    /// Enables weaker nested sandbox behavior for this function.
    /// </summary>
    public SandboxToggle EnableWeakerNestedSandbox { get; set; } = SandboxToggle.Inherit;

    /// <summary>
    /// Comma-separated violation regex patterns to ignore for this function.
    /// </summary>
    public string IgnoreViolationPatterns { get; set; } = "";

    /// <summary>
    /// Comma-separated environment variables to pass through for this function.
    /// </summary>
    public string AllowedEnvironmentVariables { get; set; } = "";

    /// <summary>
    /// Maximum depth used when discovering mandatory deny paths.
    /// </summary>
    public int MandatoryDenySearchDepth { get; set; } = -1;

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
    public string[] GetAllowWrite() => SplitCommaSeparated(AllowWrite);

    /// <summary>
    /// Parses DenyRead into an array.
    /// </summary>
    public string[] GetDenyRead() => SplitCommaSeparated(DenyRead);

    /// <summary>
    /// Parses AllowRead into an array.
    /// </summary>
    public string[] GetAllowRead() => SplitCommaSeparated(AllowRead);

    /// <summary>
    /// Parses DenyWrite into an array.
    /// </summary>
    public string[] GetDenyWrite() => SplitCommaSeparated(DenyWrite);

    /// <summary>
    /// Parses AllowUnixSockets into an array.
    /// </summary>
    public string[] GetAllowUnixSockets() => SplitCommaSeparated(AllowUnixSockets);

    /// <summary>
    /// Parses AllowMachLookup into an array.
    /// </summary>
    public string[] GetAllowMachLookup() => SplitCommaSeparated(AllowMachLookup);

    /// <summary>
    /// Parses IgnoreViolationPatterns into an array.
    /// </summary>
    public string[] GetIgnoreViolationPatterns() => SplitCommaSeparated(IgnoreViolationPatterns);

    /// <summary>
    /// Parses AllowedEnvironmentVariables into an array.
    /// </summary>
    public string[] GetAllowedEnvironmentVariables() => SplitCommaSeparated(AllowedEnvironmentVariables);

    /// <summary>
    /// Converts the attribute declaration into a sparse sandbox override.
    /// </summary>
    public SandboxConfigOverride ToSandboxConfigOverride() => new()
    {
        NetworkMode = ToNetworkMode(NetworkMode),
        AllowedDomains = ToNullable(GetAllowedDomains()),
        DeniedDomains = ToNullable(GetDeniedDomains()),
        AllowWrite = ToNullable(GetAllowWrite()),
        DenyRead = ToNullable(GetDenyRead()),
        AllowRead = ToNullable(GetAllowRead()),
        DenyWrite = ToNullable(GetDenyWrite()),
        AllowUnixSockets = ToNullable(GetAllowUnixSockets()),
        AllowMachLookup = ToNullable(GetAllowMachLookup()),
        AllowPty = ToNullable(AllowPty),
        AllowLocalBinding = ToNullable(AllowLocalBinding),
        AllowAllUnixSockets = ToNullable(AllowAllUnixSockets),
        AllowMacOSTrustdLookup = ToNullable(AllowMacOSTrustdLookup),
        AllowGitConfig = ToNullable(AllowGitConfig),
        EnableWeakerNestedSandbox = ToNullable(EnableWeakerNestedSandbox),
        IgnoreViolationPatterns = ToNullable(GetIgnoreViolationPatterns()),
        AllowedEnvironmentVariables = ToNullable(GetAllowedEnvironmentVariables()),
        MandatoryDenySearchDepth = MandatoryDenySearchDepth >= 0 ? MandatoryDenySearchDepth : null
    };

    private static SandboxNetworkMode? ToNetworkMode(SandboxNetworkPolicy policy) =>
        policy switch
        {
            SandboxNetworkPolicy.Blocked => SandboxNetworkMode.Blocked,
            SandboxNetworkPolicy.Filtered => SandboxNetworkMode.Filtered,
            SandboxNetworkPolicy.Unrestricted => SandboxNetworkMode.Unrestricted,
            _ => null
        };

    private static bool? ToNullable(SandboxToggle toggle) =>
        toggle switch
        {
            SandboxToggle.Enabled => true,
            SandboxToggle.Disabled => false,
            _ => null
        };

    private static string[]? ToNullable(string[] values) =>
        values.Length == 0 ? null : values;

    private static string[] SplitCommaSeparated(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
