namespace HPD.Agent.Sandbox.Local;

using HPD.Agent;
using Microsoft.Extensions.Logging;

/// <summary>
/// AgentBuilder extensions for local HPD Execution providers.
/// </summary>
public static class AgentBuilderLocalSandboxExtensions
{
    public static AgentBuilder WithLocalSandbox(
        this AgentBuilder builder,
        ILogger<LocalSandboxMiddleware>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Middlewares.RemoveAll(static middleware => middleware is LocalSandboxMiddleware);
        builder.Middlewares.Add(new LocalSandboxMiddleware(logger));
        return builder;
    }
}
