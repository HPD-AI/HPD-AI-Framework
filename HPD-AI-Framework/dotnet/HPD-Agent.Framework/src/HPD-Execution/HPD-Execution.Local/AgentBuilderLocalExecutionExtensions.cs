namespace HPD.Execution.Local;

using HPD.Agent;
using Microsoft.Extensions.Logging;

/// <summary>
/// AgentBuilder extensions for local HPD Execution providers.
/// </summary>
public static class AgentBuilderLocalExecutionExtensions
{
    public static AgentBuilder WithLocalExecution(
        this AgentBuilder builder,
        ILogger<LocalExecutionMiddleware>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Middlewares.RemoveAll(static middleware => middleware is LocalExecutionMiddleware);
        builder.Middlewares.Add(new LocalExecutionMiddleware(logger));
        return builder;
    }
}
