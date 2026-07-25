using HPD.Agent.Security;

namespace HPD.Agent.ToolHarness.Coding.Security;

/// <summary>
/// Captures the general agent sandbox for Coding and translates the declared
/// Coding workspace into explicit filesystem capabilities.
/// </summary>
internal static class CodingSandboxRuntime
{
    /// <summary>Captures the effective sandbox for one Coding invocation.</summary>
    public static AgentSandboxRuntime Capture(AgentRunConfig runConfig)
    {
        ArgumentNullException.ThrowIfNull(runConfig);

        var runtime = AgentSandboxRuntime.Capture(runConfig);
        if (!AgentWorkspace.TryFrom(runConfig, out var workspace, out _))
            return runtime;

        var workingDirectory = workspace.DefaultRootPath;
        var grants = runtime.Filesystem
            .Select(grant => grant with
            {
                Path = Path.IsPathFullyQualified(grant.Path)
                    ? Path.GetFullPath(grant.Path)
                    : Path.GetFullPath(Path.Combine(workingDirectory, grant.Path))
            })
            .ToList();

        foreach (var root in workspace.Roots)
        {
            AddGrant(grants, AgentSandboxPathAccess.Read, root.Path);
            AddGrant(grants, AgentSandboxPathAccess.Write, root.Path);
        }

        return runtime with
        {
            Configuration = runtime.Configuration with
            {
                Filesystem = grants.ToArray()
            }
        };
    }

    private static void AddGrant(
        List<AgentSandboxPathGrant> grants,
        AgentSandboxPathAccess access,
        string path)
    {
        var canonical = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (grants.Any(grant =>
                grant.Access == access &&
                string.Equals(grant.Path, canonical, comparison)))
        {
            return;
        }

        grants.Add(new AgentSandboxPathGrant
        {
            Access = access,
            Path = canonical
        });
    }
}
