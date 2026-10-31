using HPD.Environment.Contracts;

namespace HPD.Agent.Security;

/// <summary>Checks requested capabilities against a trusted, runtime-only host ceiling.</summary>
public static class AgentSandboxAuthorization
{
    public static void Validate(AgentSandboxConfiguration requested, AgentSandboxConfiguration ceiling)
    {
        foreach (var grant in requested.Filesystem)
        {
            if (!Path.IsPathFullyQualified(grant.Path) || !ceiling.Filesystem.Any(allowed =>
                (allowed.Access == AgentSandboxPathAccess.Write || grant.Access == AgentSandboxPathAccess.Read)
                && AgentFilesystemPath.Contains(allowed.Path, grant.Path)))
                throw new AgentRunConfigurationException("CapabilityCeilingExceeded", "Security.Sandbox.Capabilities.Filesystem", "Filesystem request exceeds host authorization.");
        }
        var network = requested.Network;
        var host = ceiling.Network;
        if (network.Mode != NetworkEgressMode.Blocked &&
            (host.Mode == NetworkEgressMode.Blocked ||
             host.Mode != NetworkEgressMode.Unrestricted && (network.Mode != host.Mode ||
                !network.AllowedDomains.All(host.AllowedDomains.Contains) || !host.DeniedDomains.All(network.DeniedDomains.Contains)) ||
             host.RequireProxyMediation && !network.RequireProxyMediation || network.ParentProxy != host.ParentProxy || network.RequestFilter != host.RequestFilter))
            throw new AgentRunConfigurationException("CapabilityCeilingExceeded", "Security.Sandbox.Capabilities.Network", "Network request exceeds host authorization.");
        var process = requested.Interactive;
        var allowedProcess = ceiling.Interactive;
        if (process.AllowPty && !allowedProcess.AllowPty || process.AllowStdin && !allowedProcess.AllowStdin ||
            process.AllowLocalBinding && !allowedProcess.AllowLocalBinding || !process.AllowedMachLookups.All(allowedProcess.AllowedMachLookups.Contains))
            throw new AgentRunConfigurationException("CapabilityCeilingExceeded", "Security.Sandbox.Capabilities.Interactive", "Process request exceeds host authorization.");
    }

    internal static AgentSandboxConfiguration Snapshot(AgentSandboxConfiguration source) => source with
    {
        Filesystem = source.Filesystem.Select(grant => grant with { }).ToArray(),
        Network = source.Network with
        {
            AllowedDomains = source.Network.AllowedDomains.Select(rule => rule with { }).ToArray(),
            DeniedDomains = source.Network.DeniedDomains.Select(rule => rule with { }).ToArray(),
            ParentProxy = source.Network.ParentProxy is null ? null : source.Network.ParentProxy with { },
            RequestFilter = source.Network.RequestFilter is null ? null : source.Network.RequestFilter with
            {
                ProviderRuleSet = source.Network.RequestFilter.ProviderRuleSet is { } extension ? extension with { Payload = extension.Payload.ToArray() } : null
            }
        },
        Interactive = source.Interactive with { AllowedMachLookups = source.Interactive.AllowedMachLookups.ToArray() }
    };
}
