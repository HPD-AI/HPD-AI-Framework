namespace HPD.Agent.Sandbox;

public interface ISandboxPolicyResolver
{
    SandboxConfig Resolve(
        SandboxConfig globalConfig,
        SandboxConfigOverride? functionOverride = null,
        SandboxConfigOverride? callOverride = null);
}

public sealed class DefaultSandboxPolicyResolver : ISandboxPolicyResolver
{
    public SandboxConfig Resolve(
        SandboxConfig globalConfig,
        SandboxConfigOverride? functionOverride = null,
        SandboxConfigOverride? callOverride = null)
    {
        ArgumentNullException.ThrowIfNull(globalConfig);

        var resolved = Merge(globalConfig, functionOverride);
        resolved = Merge(resolved, callOverride);
        resolved.Validate();
        return resolved;
    }

    private static SandboxConfig Merge(SandboxConfig baseConfig, SandboxConfigOverride? specific)
    {
        if (specific is null)
            return baseConfig;

        return baseConfig with
        {
            AllowWrite = AppendNullable(baseConfig.AllowWrite, specific.AllowWrite),
            DenyRead = AppendNullable(baseConfig.DenyRead, specific.DenyRead),
            AllowRead = AppendNullable(baseConfig.AllowRead, specific.AllowRead),
            DenyWrite = AppendNullable(baseConfig.DenyWrite, specific.DenyWrite),
            NetworkMode = specific.NetworkMode ?? baseConfig.NetworkMode,
            AllowedDomains = specific.AllowedDomains is { Length: > 0 }
                ? specific.AllowedDomains
                : baseConfig.AllowedDomains,
            DeniedDomains = AppendNullable(baseConfig.DeniedDomains, specific.DeniedDomains),
            EnableWeakerNestedSandbox = specific.EnableWeakerNestedSandbox ?? baseConfig.EnableWeakerNestedSandbox,
            MandatoryDenySearchDepth = specific.MandatoryDenySearchDepth ?? baseConfig.MandatoryDenySearchDepth,
            AllowGitConfig = specific.AllowGitConfig ?? baseConfig.AllowGitConfig,
            AllowAllUnixSockets = specific.AllowAllUnixSockets ?? baseConfig.AllowAllUnixSockets,
            AllowUnixSockets = AppendNullableArray(baseConfig.AllowUnixSockets, specific.AllowUnixSockets),
            AllowPty = specific.AllowPty ?? baseConfig.AllowPty,
            AllowLocalBinding = specific.AllowLocalBinding ?? baseConfig.AllowLocalBinding,
            AllowMacOSTrustdLookup = specific.AllowMacOSTrustdLookup ?? baseConfig.AllowMacOSTrustdLookup,
            AllowMachLookup = AppendNullable(baseConfig.AllowMachLookup, specific.AllowMachLookup),
            IgnoreViolationPatterns = AppendNullableArray(baseConfig.IgnoreViolationPatterns, specific.IgnoreViolationPatterns),
            AllowedEnvironmentVariables = AppendNullable(baseConfig.AllowedEnvironmentVariables, specific.AllowedEnvironmentVariables)
        };
    }

    private static string[] Append(string[] first, string[] second) =>
        first.Concat(second).Distinct(StringComparer.Ordinal).ToArray();

    private static string[] AppendNullable(string[] first, string[]? second) =>
        second is null || second.Length == 0
            ? first
            : Append(first, second);

    private static string[]? AppendNullableArray(string[]? first, string[]? second)
    {
        if (first is null || first.Length == 0)
            return second;
        if (second is null || second.Length == 0)
            return first;
        return Append(first, second);
    }
}
