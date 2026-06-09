namespace HPD.Agent.Sandbox.AppleVirtualization;

using HPD.Agent;
using HPD.Environment.AppleVirtualization;
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

        builder.Middlewares.RemoveAll(static middleware => middleware is AppleVirtualizationSandboxMiddleware);
        builder.Middlewares.Add(new AppleVirtualizationSandboxMiddleware(options, logger));
        return builder;
    }
}
