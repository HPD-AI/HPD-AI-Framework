namespace HPD.Environment.Virtualization.Apple;

using HPD.Environment.Contracts;

/// <summary>Creates sandbox requests for Apple Virtualization workspaces.</summary>
public static class AppleVirtualizationSandbox
{
    /// <summary>Shares a host directory with a guest whose processes may use the guest network.</summary>
    public static SandboxSessionRequest ForDirectoryWithGuestNetwork(
        string path, bool readOnly = false, string rootId = "workspace", string guestPath = "/workspace") =>
        new()
        {
            Isolation = ProcessIsolationPolicy.Default with
            {
                Mode = ProcessIsolationMode.Isolated,
                Network = new NetworkEgressPolicy { Mode = NetworkEgressMode.Unrestricted },
                Filesystem = FilesystemAccessPolicy.Default with
                {
                    DangerousPaths = DangerousPathPolicy.Default with { ProtectSensitiveDefaults = false },
                },
            },
            Workspaces =
            [
                SandboxWorkspaceRequest.HostDirectory(path, readOnly ? AccessMode.ReadOnly : AccessMode.ReadWrite, rootId, guestPath)
            ],
        };
}
