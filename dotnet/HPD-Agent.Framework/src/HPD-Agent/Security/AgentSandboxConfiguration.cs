using HPD.Environment.Contracts;

namespace HPD.Agent.Security;

/// <summary>
/// Host-supplied sandbox capabilities shared by every process and in-process harness.
/// </summary>
public sealed record AgentSandboxConfiguration
{
    /// <summary>Gets filesystem grants available without escalation.</summary>
    public IReadOnlyList<AgentSandboxPathGrant> Filesystem { get; init; } = [];

    /// <summary>Gets the network-egress policy available without escalation.</summary>
    public NetworkEgressPolicy Network { get; init; } = NetworkEgressPolicy.Blocked;

    /// <summary>Gets interactive process capabilities available without escalation.</summary>
    public ProcessInteractivePolicy Interactive { get; init; } = ProcessInteractivePolicy.Default;

    /// <summary>Creates the process-isolation policy for one working directory.</summary>
    public ProcessIsolationPolicy CreateProcessIsolationPolicy(
        AgentSecurityProfile security,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        return ProcessIsolationPolicy.Default with
        {
            Mode = security.Sandbox switch
            {
                AgentSandboxPolicy.Enforced => ProcessIsolationMode.Isolated,
                AgentSandboxPolicy.Disabled => ProcessIsolationMode.Disabled,
                _ => throw new InvalidOperationException(
                    $"Agent sandbox policy '{security.Sandbox}' is not supported.")
            },
            Filesystem = new FilesystemAccessPolicy
            {
                Rules = Filesystem.Select(grant => grant.ToRule(workingDirectory)).ToArray()
            },
            Network = Network,
            Interactive = Interactive
        };
    }
}

/// <summary>One filesystem capability granted to an agent sandbox.</summary>
public sealed record AgentSandboxPathGrant
{
    /// <summary>Gets the granted access kind.</summary>
    public required AgentSandboxPathAccess Access { get; init; }

    /// <summary>Gets the absolute path or a path relative to the process working directory.</summary>
    public required string Path { get; init; }

    internal PathAccessRule ToRule(string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        var canonical = System.IO.Path.IsPathFullyQualified(Path)
            ? System.IO.Path.GetFullPath(Path)
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(workingDirectory, Path));
        return new PathAccessRule
        {
            Kind = Access switch
            {
                AgentSandboxPathAccess.Read => PathAccessRuleKind.AllowRead,
                AgentSandboxPathAccess.Write => PathAccessRuleKind.AllowWrite,
                _ => throw new InvalidOperationException(
                    $"Sandbox path access '{Access}' is not supported.")
            },
            Path = new HostPath(canonical),
            Reason = "Agent sandbox capability"
        };
    }
}

/// <summary>Identifies filesystem access granted to an agent sandbox.</summary>
public enum AgentSandboxPathAccess
{
    Read,
    Write
}

/// <summary>Immutable sandbox state captured for one run.</summary>
public sealed record AgentSandboxRuntime
{
    /// <summary>Gets the secure default runtime.</summary>
    public static AgentSandboxRuntime Default { get; } = Capture(new AgentRunConfig());

    /// <summary>Gets the run security profile.</summary>
    public AgentSecurityProfile Security { get; init; } = new();

    /// <summary>Gets the capabilities supplied by the host.</summary>
    public AgentSandboxConfiguration Configuration { get; init; } = new();

    /// <summary>Gets whether host isolation is enforced.</summary>
    public bool IsEnforced => Security.Sandbox == AgentSandboxPolicy.Enforced;

    /// <summary>Gets the configured filesystem grants.</summary>
    public IReadOnlyList<AgentSandboxPathGrant> Filesystem => Configuration.Filesystem;

    /// <summary>Gets the configured network policy.</summary>
    public NetworkEgressPolicy Network => Configuration.Network;

    /// <summary>Gets the configured interactive process policy.</summary>
    public ProcessInteractivePolicy Interactive => Configuration.Interactive;

    /// <summary>Captures sandbox state from a run configuration.</summary>
    public static AgentSandboxRuntime Capture(AgentRunConfig runConfig)
    {
        ArgumentNullException.ThrowIfNull(runConfig);
        return new AgentSandboxRuntime
        {
            Security = runConfig.Security with { },
            Configuration = runConfig.Sandbox with
            {
                Filesystem = runConfig.Sandbox.Filesystem
                    .Select(static grant => grant with { })
                    .ToArray()
            }
        };
    }

    /// <summary>Creates a low-level process policy for one working directory.</summary>
    public ProcessIsolationPolicy ToProcessIsolationPolicy(string workingDirectory)
        => Configuration.CreateProcessIsolationPolicy(Security, workingDirectory);

    /// <summary>Returns a runtime with one additional narrow filesystem grant.</summary>
    public AgentSandboxRuntime WithPathGrant(
        AgentSandboxPathAccess access,
        string path)
    {
        var canonical = Path.GetFullPath(path);
        var grant = new AgentSandboxPathGrant { Access = access, Path = canonical };
        return this with
        {
            Configuration = Configuration with
            {
                Filesystem =
                [
                    .. Configuration.Filesystem.Where(existing =>
                        existing.Access != access ||
                        !Path.IsPathFullyQualified(existing.Path) ||
                        !Path.GetFullPath(existing.Path).Equals(
                            canonical,
                            OperatingSystem.IsWindows()
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal)),
                    grant
                ]
            }
        };
    }
}
