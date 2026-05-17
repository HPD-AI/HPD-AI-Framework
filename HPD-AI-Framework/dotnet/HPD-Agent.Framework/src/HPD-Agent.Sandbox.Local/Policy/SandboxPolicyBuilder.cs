using HPD.Agent.Sandbox;

namespace HPD.Sandbox.Local.Policy;

internal static class SandboxPolicyBuilder
{
    public static SandboxPolicy FromConfig(SandboxConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new SandboxPolicy
        {
            Network = BuildNetworkPolicy(config),
            Filesystem = new FilesystemPolicy
            {
                DenyRead = config.DenyRead,
                AllowRead = config.AllowRead,
                AllowWrite = config.AllowWrite,
                DenyWrite = config.DenyWrite,
                AllowGitConfig = config.AllowGitConfig,
            },
            UnixSockets = new UnixSocketPolicy
            {
                AllowAll = config.AllowAllUnixSockets,
                AllowedPaths = config.AllowUnixSockets ?? [],
            },
            EnableWeakerNestedSandbox = config.EnableWeakerNestedSandbox,
            EnableViolationMonitoring = config.EnableViolationMonitoring,
            AllowPty = config.AllowPty,
            AllowLocalBinding = config.AllowLocalBinding,
            AllowMacOSTrustdLookup = config.AllowMacOSTrustdLookup,
            MandatoryDenySearchDepth = config.MandatoryDenySearchDepth,
            ExternalHttpProxyPort = config.ExternalHttpProxyPort,
            ExternalSocksProxyPort = config.ExternalSocksProxyPort,
        };
    }

    public static NetworkPolicy BuildNetworkPolicy(SandboxConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return BuildNetworkPolicy(
            config.NetworkMode,
            config.AllowedDomains,
            config.DeniedDomains);
    }

    public static NetworkPolicy BuildNetworkPolicy(
        SandboxNetworkMode networkMode,
        string[] allowedDomains,
        string[] deniedDomains)
    {
        return networkMode switch
        {
            SandboxNetworkMode.Unrestricted => NetworkPolicy.Unrestricted,
            SandboxNetworkMode.Blocked => NetworkPolicy.Blocked,
            SandboxNetworkMode.Filtered => NetworkPolicy.Filtered(
                allowedDomains.Select(DomainPattern.Parse).ToArray(),
                deniedDomains.Select(DomainPattern.Parse).ToArray()),
            _ => throw new ArgumentOutOfRangeException(nameof(networkMode), networkMode, "Unknown network mode."),
        };
    }
}
