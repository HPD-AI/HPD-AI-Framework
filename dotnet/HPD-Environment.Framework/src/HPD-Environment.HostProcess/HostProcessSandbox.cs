namespace HPD.Environment.HostProcess;

using HPD.Environment.Contracts;

/// <summary>Creates sandbox requests for live Host Process workspaces.</summary>
public static class HostProcessSandbox
{
    /// <summary>Exposes a host directory to the sandbox.</summary>
    public static SandboxSessionRequest ForDirectory(string path, bool readOnly = false, string rootId = "workspace") =>
        new()
        {
            Isolation = ProcessIsolationPolicy.Default,
            Workspaces =
            [
                SandboxWorkspaceRequest.HostDirectory(path, readOnly ? AccessMode.ReadOnly : AccessMode.ReadWrite, rootId)
            ],
        };
}
