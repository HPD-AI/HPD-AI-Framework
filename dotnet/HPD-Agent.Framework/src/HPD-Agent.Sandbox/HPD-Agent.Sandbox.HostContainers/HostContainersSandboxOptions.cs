namespace HPD.Agent.Sandbox.HostContainers;

using HPD.Environment.Contracts;

/// <summary>One workspace visible inside a retained sandbox container.</summary>
public sealed record HostContainersWorkspaceOptions
{
    /// <summary>
    /// Host directory source for <see cref="SourceKind.HostPath"/>; omitted for
    /// a provider-owned source already present in the container.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>Selects how workspace content reaches the container.</summary>
    public HostContainersWorkspaceSourceKind SourceKind { get; init; } =
        HostContainersWorkspaceSourceKind.HostPath;

    /// <summary>
    /// Stable provider-owned content identity for
    /// <see cref="HostContainersWorkspaceSourceKind.ProviderSource"/>.
    /// </summary>
    public string? ProviderSourceId { get; init; }

    /// <summary>Stable logical root identifier.</summary>
    public string RootId { get; init; } = "workspace";

    /// <summary>Human-readable root name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Absolute destination directory in the container.</summary>
    public string ContainerPath { get; init; } = "/workspace";

    /// <summary>Access granted to model-facing tools and processes.</summary>
    public AccessMode Access { get; init; } = AccessMode.ReadWrite;
}

/// <summary>Content ownership model for one Host Containers workspace.</summary>
public enum HostContainersWorkspaceSourceKind
{
    /// <summary>The workspace is a host directory mounted into the container.</summary>
    HostPath,

    /// <summary>
    /// The workspace already resides in provider-owned container storage, such
    /// as an image layer, volume, or engine-side projection.
    /// </summary>
    ProviderSource,

    /// <summary>
    /// Host content is copied into provider-owned container storage when the
    /// session starts and copied back before it is disposed. This profile
    /// currently requires <see cref="AccessMode.ReadWrite"/> so the staging
    /// workspace can be materialized before the model-facing session opens.
    /// Copy-out creates or overwrites host files but does not infer deletion of
    /// unrelated host files.
    /// </summary>
    CopyInOut,
}

/// <summary>Configuration for a retained Docker-compatible sandbox container.</summary>
public sealed record HostContainersSandboxOptions
{
    /// <summary>Container image already available to the local engine.</summary>
    public required string Image { get; init; }

    /// <summary>Explicit local Docker-compatible engine Unix socket.</summary>
    public required string EngineSocketPath { get; init; }

    /// <summary>Optional Docker CLI executable path.</summary>
    public string? DockerCliPath { get; init; }

    /// <summary>Provider-private workload state directory.</summary>
    public string? WorkloadStateRoot { get; init; }

    /// <summary>Workspace bind mounts created with the retained container.</summary>
    public IReadOnlyList<HostContainersWorkspaceOptions> Workspaces { get; init; } =
        [];

    /// <summary>Process policy applied to commands in the container.</summary>
    public ProcessIsolationPolicy Isolation { get; init; } =
        ProcessIsolationPolicy.Default with
        {
            Mode = ProcessIsolationMode.Isolated,
            Violations =
                ProcessViolationPolicy.Default with
                {
                    Action =
                        ProcessViolationAction.ObserveOnly,
                },
            Degradation =
                ProcessIsolationDegradationPolicy.FailClosed with
                {
                    Mode =
                        ProcessIsolationDegradationMode
                            .AllowProviderDegraded,
                    AllowDegradedFeatures =
                    [
                        StandardSandboxEnvironmentCapabilities
                            .SymlinkHandling.Value,
                    ],
                },
        };

    /// <summary>Resource ceilings applied to the retained container.</summary>
    public ResourceQuotaPolicy Resources { get; init; } =
        ResourceQuotaPolicy.Default;
}
