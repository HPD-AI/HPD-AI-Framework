using System.Threading.Channels;
using HPD.Agent.Sandbox.ProcessIsolation;

namespace HPD.Agent.Sandbox.Platforms;

/// <summary>
/// Platform-specific process-isolation backend.
/// </summary>
internal interface ISandboxBackend : IAsyncDisposable
{
    /// <summary>
    /// Wraps a command with platform-specific process-isolation restrictions.
    /// </summary>
    Task<PreparedSandboxCommand> WrapCommandAsync(CommandInvocation command, CancellationToken cancellationToken);

    /// <summary>
    /// Wraps a command with provider-native process isolation plan restrictions.
    /// </summary>
    Task<PreparedSandboxCommand> WrapCommandAsync(
        CommandInvocation command,
        SandboxIsolationPlan plan,
        CancellationToken cancellationToken);

    /// <summary>
    /// Wraps a pre-rendered shell command with platform-specific process-isolation restrictions.
    /// </summary>
    Task<string> WrapCommandAsync(string command, CancellationToken cancellationToken);

    /// <summary>
    /// Violation event stream (null if not supported on this platform).
    /// </summary>
    ChannelReader<ProcessIsolationViolation>? Violations { get; }

    /// <summary>
    /// Checks required OS tools and optional weaker layers, returning structured diagnostics.
    /// </summary>
    Task<ProcessIsolationDependencyCheck> GetDependencyCheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if required OS tools are available.
    /// </summary>
    Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken);
}
