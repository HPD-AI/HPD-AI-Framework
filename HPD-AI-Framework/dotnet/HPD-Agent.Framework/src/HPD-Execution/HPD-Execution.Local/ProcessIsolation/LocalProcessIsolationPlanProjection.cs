namespace HPD.Execution.Local.ProcessIsolation;

using HPD.Execution.Contracts;

internal static class LocalProcessIsolationPlanProjection
{
    public static IReadOnlyList<string> AllowWritePaths(this LocalFilesystemIsolationPlan filesystem) =>
        Paths(filesystem, PathAccessRuleKind.AllowWrite);

    public static IReadOnlyList<string> DenyReadPaths(this LocalFilesystemIsolationPlan filesystem) =>
        Paths(filesystem, PathAccessRuleKind.DenyRead);

    public static IReadOnlyList<string> AllowReadPaths(this LocalFilesystemIsolationPlan filesystem) =>
        Paths(filesystem, PathAccessRuleKind.AllowRead);

    public static IReadOnlyList<string> DenyWritePaths(this LocalFilesystemIsolationPlan filesystem) =>
        Paths(filesystem, PathAccessRuleKind.DenyWrite);

    public static IReadOnlyList<string> AllowedDomainPatterns(this LocalNetworkIsolationPlan network) =>
        network.AllowedDomains.Select(rule => rule.Source.Pattern).ToArray();

    public static IReadOnlyList<string> DeniedDomainPatterns(this LocalNetworkIsolationPlan network) =>
        network.DeniedDomains.Select(rule => rule.Source.Pattern).ToArray();

    public static IReadOnlyList<string> AllowedUnixSocketPaths(this LocalUnixSocketIsolationPlan unixSockets) =>
        unixSockets.AllowedSockets.Select(rule => rule.Path.Value).ToArray();

    private static IReadOnlyList<string> Paths(LocalFilesystemIsolationPlan filesystem, PathAccessRuleKind kind) =>
        filesystem.Rules
            .Where(rule => rule.Kind == kind)
            .Select(rule => rule.Path.Value)
            .ToArray();
}
