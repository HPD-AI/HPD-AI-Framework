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
        if (runConfig.Context?.Properties?.ContainsKey(AgentWorkspace.ContextKey) != true) return runtime;
        var workspace = AgentWorkspace.From(runConfig);
        if (!runtime.IsEnforced && workspace.Roots.Any(root => root.Access == AgentWorkspaceAccess.ReadOnly))
            throw new InvalidOperationException("Read-only workspaces require enforced process isolation.");

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
            if (root.Access == AgentWorkspaceAccess.ReadWrite)
                AddGrant(grants, AgentSandboxPathAccess.Write, root.Path);
            else if (grants.Any(grant => grant.Access == AgentSandboxPathAccess.Write &&
                (AgentFilesystemPath.Contains(grant.Path, root.Path) || AgentFilesystemPath.Contains(root.Path, grant.Path))))
                throw new InvalidOperationException("Write grants overlap a declared read-only workspace root.");
        }

        if (runConfig.Security.CapabilityCeiling is { } ceiling)
            AgentSandboxAuthorization.Validate(runtime.Configuration with { Filesystem = grants.ToArray() }, ceiling);
        return runtime with
        {
            ReadOnlyPaths = workspace.Roots.Where(root => root.Access == AgentWorkspaceAccess.ReadOnly).Select(root => root.Path).ToArray(),
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
