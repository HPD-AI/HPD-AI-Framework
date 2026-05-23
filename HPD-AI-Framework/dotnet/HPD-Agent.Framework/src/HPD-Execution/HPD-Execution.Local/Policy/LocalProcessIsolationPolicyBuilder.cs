using HPD.Execution.Contracts;

namespace HPD.Execution.Local.Policy;

internal static class LocalProcessIsolationPolicyBuilder
{
    public static NetworkPolicy BuildNetworkPolicy(
        NetworkEgressMode networkMode,
        IReadOnlyList<string> allowedDomains,
        IReadOnlyList<string> deniedDomains)
    {
        return networkMode switch
        {
            NetworkEgressMode.Unrestricted => NetworkPolicy.Unrestricted,
            NetworkEgressMode.Blocked => NetworkPolicy.Blocked,
            NetworkEgressMode.Filtered => NetworkPolicy.Filtered(
                allowedDomains.Select(DomainPattern.Parse).ToArray(),
                deniedDomains.Select(DomainPattern.Parse).ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(networkMode), networkMode, "Unknown network mode."),
        };
    }

    public static ProcessIsolationPolicy ToProcessIsolationPolicy(this LocalProcessIsolationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new ProcessIsolationPolicy
        {
            Filesystem = new FilesystemAccessPolicy
            {
                Rules = policy.Filesystem.AllowWrite.Select(path => Rule(PathAccessRuleKind.AllowWrite, path))
                    .Concat(policy.Filesystem.DenyRead.Select(path => Rule(PathAccessRuleKind.DenyRead, path)))
                    .Concat(policy.Filesystem.AllowRead.Select(path => Rule(PathAccessRuleKind.AllowRead, path)))
                    .Concat(policy.Filesystem.DenyWrite.Select(path => Rule(PathAccessRuleKind.DenyWrite, path)))
                    .ToArray(),
            },
            Network = new NetworkEgressPolicy
            {
                Mode = policy.Network.Mode,
                AllowedDomains = policy.Network.AllowedDomains
                    .Select(pattern => new DomainRule { Pattern = pattern.Raw, Kind = DomainRuleKind.ProviderValidate })
                    .ToArray(),
                DeniedDomains = policy.Network.DeniedDomains
                    .Select(pattern => new DomainRule { Pattern = pattern.Raw, Kind = DomainRuleKind.ProviderValidate })
                    .ToArray(),
            },
            UnixSockets = new UnixSocketAccessPolicy
            {
                AllowAll = policy.UnixSockets.AllowAll,
                AllowedSockets = policy.UnixSockets.AllowedPaths
                    .Select(path => new UnixSocketAccessRule { Path = new UnixSocketPath(path) })
                    .ToArray(),
            },
            Environment = new EnvironmentAccessPolicy
            {
                AllowedVariables = [],
            },
            Interactive = new ProcessInteractivePolicy
            {
                AllowPty = policy.AllowPty,
                AllowLocalBinding = policy.AllowLocalBinding,
                AllowedMachLookups = policy.AllowMacOSTrustdLookup
                    ? policy.AllowMacOSTrustdLookup.Yield("com.apple.trustd")
                    : [],
            },
            Violations = new ProcessViolationPolicy
            {
                Action = policy.EnableViolationMonitoring
                    ? ProcessViolationAction.ObserveAndFailInvocation
                    : ProcessViolationAction.ProviderDefault,
            },
        };
    }

    private static PathAccessRule Rule(PathAccessRuleKind kind, string path) => new()
    {
        Kind = kind,
        Path = new HostPath(path),
    };

    private static IReadOnlyList<T> Yield<T>(this bool condition, T value) =>
        condition ? [value] : [];
}
