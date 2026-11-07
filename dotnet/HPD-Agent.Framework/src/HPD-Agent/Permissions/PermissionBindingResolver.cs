using Microsoft.Extensions.AI;
using HPD.Agent.ClientTools;

namespace HPD.Agent.Permissions;

/// <summary>Resolves static and host overrides before middleware admission.</summary>
internal static class PermissionBindingResolver
{
    internal static ResolvedFunction Resolve(AIFunction function, ResolvedFunctionInvocation? invocation, AgentRunConfig runConfig, PermissionOverrideRegistry? overrides)
    {
        if (function is not HPDAIFunctionFactory.HPDAIFunction hpd)
            throw new InvalidOperationException("UnsupportedFunctionPermissionStatus: register a normalized function.");
        var declaration = ResolveDeclaration(hpd, invocation?.Action);
        var authority = declaration?.Authority ?? $"function/{Uri.EscapeDataString(hpd.Name)}";
        var selector = new PermissionOverrideSelector(hpd.Name, invocation?.Action, authority);
        var runOverride = PermissionOverrideRegistry.Resolve(runConfig.Security.PermissionOverrides, selector);
        var builderOverride = overrides?.Resolve(selector);
        var required = runOverride ?? builderOverride ?? declaration?.RequiresPermission == true;
        var source = runOverride is not null ? PermissionBindingSource.RunOverride : builderOverride is not null
            ? PermissionBindingSource.Builder : invocation?.Action is null ? PermissionBindingSource.Function : PermissionBindingSource.Action;
        return new(hpd, required ? new AIFunctionPermissionBinding(
            declaration?.Authority ?? $"function/{Uri.EscapeDataString(hpd.Name)}",
            declaration?.PolicyDescriptorId ?? PermissionMiddleware.DefaultPolicyId, source) : null);
    }

    internal static AIFunctionPermissionDeclaration? ResolveDeclaration(AIFunction function, string? action)
    {
        if (function is not HPDAIFunctionFactory.HPDAIFunction hpd) return null;
        if (action is not null && hpd.AdditionalProperties?.TryGetValue("ClientToolDefinition", out var metadata) == true &&
            metadata is ClientToolDefinition tool && tool.OperationContract?.Actions.TryGetValue(action, out var clientPolicy) == true)
            return ClientToolPolicy.Resolve(tool.DefaultPolicy, clientPolicy).Permission;
        return action is not null && hpd.HPDOptions.OperationContract?.Actions.TryGetValue(action, out var policy) == true
            ? policy.Permission : hpd.HPDOptions.FunctionPermission;
    }
}
