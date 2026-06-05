namespace HPD.Agent.Sandbox.ProcessIsolation;

using HPD.Execution.Contracts;

internal static class SandboxPlanProjection
{
    public static IReadOnlyList<string> AllowWritePaths(this SandboxFilesystemIsolationPlan filesystem) =>
        Paths(filesystem, PathAccessRuleKind.AllowWrite);

    public static IReadOnlyList<string> DenyReadPaths(this SandboxFilesystemIsolationPlan filesystem) =>
        Paths(filesystem, PathAccessRuleKind.DenyRead);

    public static IReadOnlyList<string> AllowReadPaths(this SandboxFilesystemIsolationPlan filesystem) =>
        Paths(filesystem, PathAccessRuleKind.AllowRead);

    public static IReadOnlyList<string> DenyWritePaths(this SandboxFilesystemIsolationPlan filesystem) =>
        Paths(filesystem, PathAccessRuleKind.DenyWrite);

    public static IReadOnlyList<string> AllowedDomainPatterns(this SandboxNetworkIsolationPlan network) =>
        network.AllowedDomains.Select(rule => rule.Source.Pattern).ToArray();

    public static IReadOnlyList<string> DeniedDomainPatterns(this SandboxNetworkIsolationPlan network) =>
        network.DeniedDomains.Select(rule => rule.Source.Pattern).ToArray();

    public static IReadOnlyList<string> AllowedUnixSocketPaths(this SandboxUnixSocketIsolationPlan unixSockets) =>
        unixSockets.AllowedSockets.Select(rule => rule.Path.Value).ToArray();

    private static IReadOnlyList<string> Paths(SandboxFilesystemIsolationPlan filesystem, PathAccessRuleKind kind) =>
        filesystem.Rules
            .Where(rule => rule.Kind == kind)
            .Select(rule => rule.Path.Value)
            .ToArray();
}
