namespace HPD.Agent.Sandbox.HostProcess;

using HPD.Agent;
using HPD.Environment.Contracts;

/// <summary>Options for one direct host workspace exposed to coding tools.</summary>
public sealed record HostProcessWorkspaceOptions
{
    /// <summary>Existing host directory to expose.</summary>
    public required string Path { get; init; }

    /// <summary>Stable logical root identifier used by model-facing tools.</summary>
    public string RootId { get; init; } = "workspace";

    /// <summary>Human-readable root name used in diagnostics.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Access granted to model-facing file tools.</summary>
    public AccessMode Access { get; init; } = AccessMode.ReadWrite;
}

/// <summary>
/// Host Process sandbox session composition options. A workspace list is
/// required to publish a real workspace binding to file-aware tools.
/// </summary>
public sealed record HostProcessSandboxOptions
{
    /// <summary>Process isolation applied to command execution.</summary>
    public ProcessIsolationPolicy Isolation { get; init; } =
        ProcessIsolationPolicy.Default;

    /// <summary>Host workspaces projected into the same execution session.</summary>
    public IReadOnlyList<HostProcessWorkspaceOptions> Workspaces { get; init; } =
        [];

    /// <summary>
    /// Resolves workspaces from the run configuration used to start an agent
    /// runtime. When set, this takes precedence over <see cref="Workspaces"/>.
    /// </summary>
    public Func<AgentRunConfig?, IReadOnlyList<HostProcessWorkspaceOptions>>?
        WorkspaceOptionsFactory { get; init; }
}
