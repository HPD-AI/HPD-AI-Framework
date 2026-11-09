namespace HPD.Agent.Sandbox.HostContainers;

using HPD.Agent;
using Microsoft.Extensions.Logging;

/// <summary>AgentBuilder extensions for the retained Host Containers backend.</summary>
public static class AgentBuilderHostContainersSandboxExtensions
{
    /// <summary>Selects the retained Host Containers sandbox backend.</summary>
    public static AgentBuilder WithHostContainersSandbox(
        this AgentBuilder builder,
        HostContainersSandboxOptions options,
        ILogger<HostContainersSandboxMiddleware>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        return builder.ReplaceMiddleware<HostContainersSandboxMiddleware>(
            new HostContainersSandboxMiddleware(
                options,
                logger));
    }
}
