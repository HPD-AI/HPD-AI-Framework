using System.Threading.Channels;
using HPD.Agent.Sandbox;

namespace HPD.Sandbox.Local.Platforms;

/// <summary>
/// Platform-specific sandbox implementation.
/// Internal interface - not exposed to library consumers.
/// </summary>
internal interface IPlatformSandbox : IAsyncDisposable
{
    /// <summary>
    /// Wraps a command with platform-specific sandbox restrictions.
    /// </summary>
    Task<SandboxedCommand> WrapCommandAsync(CommandInvocation command, CancellationToken cancellationToken);

    /// <summary>
    /// Wraps a pre-rendered shell command with platform-specific sandbox restrictions.
    /// </summary>
    Task<string> WrapCommandAsync(string command, CancellationToken cancellationToken);

    /// <summary>
    /// Violation event stream (null if not supported on this platform).
    /// </summary>
    ChannelReader<SandboxViolation>? Violations { get; }

    /// <summary>
    /// Checks required OS tools and optional weaker layers, returning structured diagnostics.
    /// </summary>
    Task<SandboxDependencyCheck> GetDependencyCheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks if required OS tools are available.
    /// </summary>
    Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken);
}
