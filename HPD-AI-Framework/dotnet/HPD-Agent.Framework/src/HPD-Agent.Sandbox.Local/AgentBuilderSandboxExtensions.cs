using HPD.Agent;
using HPD.Agent.Sandbox;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local;

/// <summary>
/// Convenience methods for enabling local OS sandboxing on an agent.
/// </summary>
public static class AgentBuilderSandboxExtensions
{
    /// <summary>
    /// Enables local sandboxing with the restrictive default configuration.
    /// </summary>
    public static AgentBuilder WithSandbox(this AgentBuilder builder)
        => builder.WithSandbox(SandboxConfig.CreateDefault());

    /// <summary>
    /// Enables local sandboxing with a configuration derived from the restrictive default.
    /// </summary>
    public static AgentBuilder WithSandbox(
        this AgentBuilder builder,
        Func<SandboxConfig, SandboxConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return builder.WithSandbox(configure(SandboxConfig.CreateDefault()));
    }

    /// <summary>
    /// Enables local sandboxing with the specified global sandbox configuration.
    /// </summary>
    public static AgentBuilder WithSandbox(
        this AgentBuilder builder,
        SandboxConfig config,
        ILogger<SandboxMiddleware>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(config);

        config.Validate();
        builder.Middlewares.RemoveAll(static middleware => middleware is SandboxMiddleware);
        builder.Middlewares.Add(new SandboxMiddleware(config, logger));
        return builder;
    }
}
