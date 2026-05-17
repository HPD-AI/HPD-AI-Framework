namespace HPD.Agent.Sandbox;

/// <summary>
/// Sparse sandbox policy override for a function invocation or individual process.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SandboxConfig"/> is the complete runtime policy supplied by the
/// host. This type represents only the values that a function or call wants to
/// change. Null arrays, nullable booleans, and nullable scalar values mean
/// "inherit the runtime policy."
/// </para>
/// </remarks>
public sealed record SandboxConfigOverride
{
    public SandboxNetworkMode? NetworkMode { get; init; }

    public string[]? AllowedDomains { get; init; }

    public string[]? DeniedDomains { get; init; }

    public string[]? AllowWrite { get; init; }

    public string[]? DenyRead { get; init; }

    public string[]? AllowRead { get; init; }

    public string[]? DenyWrite { get; init; }

    public bool? EnableWeakerNestedSandbox { get; init; }

    public int? MandatoryDenySearchDepth { get; init; }

    public bool? AllowGitConfig { get; init; }

    public bool? AllowAllUnixSockets { get; init; }

    public string[]? AllowUnixSockets { get; init; }

    public bool? AllowPty { get; init; }

    public bool? AllowLocalBinding { get; init; }

    public bool? AllowMacOSTrustdLookup { get; init; }

    public string[]? AllowMachLookup { get; init; }

    public string[]? IgnoreViolationPatterns { get; init; }

    public string[]? AllowedEnvironmentVariables { get; init; }
}

/// <summary>
/// Attribute-friendly network policy value where <see cref="Inherit"/> means
/// "do not override the runtime policy."
/// </summary>
public enum SandboxNetworkPolicy
{
    Inherit,
    Blocked,
    Filtered,
    Unrestricted
}

/// <summary>
/// Attribute-friendly tri-state boolean.
/// </summary>
public enum SandboxToggle
{
    Inherit,
    Disabled,
    Enabled
}
