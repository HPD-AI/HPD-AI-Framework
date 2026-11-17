namespace HPD.Environment.HostContainers;

using HPD.Environment.Contracts;

/// <summary>Creates sandbox requests for retained Host Containers workspaces.</summary>
public static class HostContainersSandbox
{
    /// <summary>Bind-mounts a host directory into a container with blocked network egress.</summary>
    /// <remarks>Container processes can follow symlinks within their mounted view. Workspace file tools deny symlinks.</remarks>
    public static SandboxSessionRequest ForDirectory(
        string path, bool readOnly = false, string rootId = "workspace", string containerPath = "/workspace") =>
        new()
        {
            Isolation = ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Isolated,
                Degradation = ProcessIsolationDegradationPolicy.FailClosed with
                {
                    Mode = ProcessIsolationDegradationMode.AllowProviderDegraded,
                    AllowDegradedFeatures = [StandardSandboxEnvironmentCapabilities.SymlinkHandling.Value],
                },
            },
            Workspaces =
            [
                SandboxWorkspaceRequest.HostDirectory(path, readOnly ? AccessMode.ReadOnly : AccessMode.ReadWrite, rootId, containerPath)
            ],
        };
}
