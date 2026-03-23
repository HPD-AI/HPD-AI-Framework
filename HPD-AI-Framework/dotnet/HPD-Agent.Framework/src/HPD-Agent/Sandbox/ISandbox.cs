namespace HPD.Agent.Sandbox;

/// <summary>
/// Abstraction for sandbox implementations.
/// Concrete implementations are in separate HPD.Sandbox.* packages.
/// </summary>
public interface ISandbox : IAsyncDisposable
{
    /// <summary>
    /// The isolation tier this sandbox provides.
    /// </summary>
    SandboxTier Tier { get; }

    /// <summary>
    /// Wraps a command with sandbox restrictions.
    /// Returns a SandboxedCommand compatible with StdioClientTransport.
    /// </summary>
    /// <param name="command">The original command (e.g., "npx")</param>
    /// <param name="args">The original arguments</param>
    /// <param name="config">Sandbox configuration for restrictions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Wrapped command with sandbox executable and modified arguments</returns>
    Task<SandboxedCommand> WrapCommandAsync(
        string command,
        IEnumerable<string> args,
        SandboxConfig config,
        CancellationToken cancellationToken = default);
}
