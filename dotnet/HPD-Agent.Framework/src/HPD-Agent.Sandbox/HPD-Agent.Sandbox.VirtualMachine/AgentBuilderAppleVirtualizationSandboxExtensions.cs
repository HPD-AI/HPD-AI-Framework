namespace HPD.Agent.Sandbox.VirtualMachine;

using HPD.Agent;
using HPD.Environment.Virtualization.Apple;
using Microsoft.Extensions.Logging;

/// <summary>
/// AgentBuilder extensions for Apple Virtualization sandbox-aware HPD Execution providers.
/// </summary>
public static class AgentBuilderAppleVirtualizationSandboxExtensions
{
    public static AgentBuilder WithAppleVirtualizationSandbox(
        this AgentBuilder builder,
        AppleVirtualizationProviderOptions? options = null,
        ILogger<AppleVirtualizationSandboxMiddleware>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ReplaceMiddleware<AppleVirtualizationSandboxMiddleware>(
            new AppleVirtualizationSandboxMiddleware(options, logger));
    }
}
