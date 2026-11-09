namespace HPD.Agent.Sandbox.HostProcess;

using HPD.Agent;
using Microsoft.Extensions.Logging;

/// <summary>
/// AgentBuilder extensions for the Host Process sandbox backend.
/// </summary>
public static class AgentBuilderHostProcessSandboxExtensions
{
    /// <summary>Selects the Environment Host Process sandbox backend.</summary>
    public static AgentBuilder WithHostProcessSandbox(
        this AgentBuilder builder,
        ILogger<HostProcessSandboxMiddleware>? logger = null)
        => builder.WithHostProcessSandbox(
            new HostProcessSandboxOptions(),
            logger);

    /// <summary>
    /// Selects Host Process and configures the workspaces exposed to coding
    /// tools and command execution.
    /// </summary>
    public static AgentBuilder WithHostProcessSandbox(
        this AgentBuilder builder,
        HostProcessSandboxOptions options,
        ILogger<HostProcessSandboxMiddleware>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);

        return builder.ReplaceMiddleware<HostProcessSandboxMiddleware>(
            new HostProcessSandboxMiddleware(logger, options));
    }
}
