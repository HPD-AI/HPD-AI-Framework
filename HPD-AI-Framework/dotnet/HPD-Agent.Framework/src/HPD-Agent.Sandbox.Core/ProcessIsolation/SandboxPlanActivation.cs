namespace HPD.Agent.Sandbox.ProcessIsolation;

using HPD.Execution.Contracts;

internal static class SandboxPlanActivation
{
    public static string CreateActivationKey(this SandboxIsolationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return string.Join(
            '\u001f',
            plan.Network.Mode,
            string.Join('\u001e', plan.Network.AllowedDomainPatterns()),
            string.Join('\u001e', plan.Network.DeniedDomainPatterns()),
            plan.Network.ParentProxy?.ProxyUri?.ToString() ?? string.Empty,
            plan.Network.ParentProxy?.AllowEnvironmentProxy.ToString() ?? string.Empty,
            plan.Tls.Mode,
            plan.Tls.InjectTrustEnvironmentVariables.ToString(),
            plan.UnixSockets.AllowAll.ToString(),
            string.Join('\u001e', plan.UnixSockets.AllowedUnixSocketPaths()),
            plan.Interactive.AllowPty.ToString(),
            plan.Interactive.AllowLocalBinding.ToString(),
            string.Join('\u001e', plan.Interactive.AllowedMachLookups));
    }
}
