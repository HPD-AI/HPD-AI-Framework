using HPD.Events;

namespace HPD.Agent.Sandbox;

/// <summary>
/// Runs local processes through the active sandbox execution boundary.
/// </summary>
public interface ISandboxedProcessRunner
{
    Task<ISandboxedProcessHandle> StartAsync(
        SandboxedProcessCommand command,
        SandboxConfigOverride? configOverride = null,
        SandboxedProcessOptions? options = null,
        CancellationToken cancellationToken = default);
}

public interface ISandboxedProcessHandle : IAsyncDisposable
{
    string ProcessId { get; }

    int? SystemProcessId { get; }

    SandboxedProcessCommand Command { get; }

    SandboxedProcessOptions Options { get; }

    IEventCoordinator Events { get; }

    Task<SandboxedProcessResult> Completion { get; }

    Task StopAsync(
        SandboxedProcessStopReason reason = SandboxedProcessStopReason.Requested,
        CancellationToken cancellationToken = default);
}

public static class SandboxedProcessRunnerExtensions
{
    public static async Task<SandboxedProcessResult> RunAsync(
        this ISandboxedProcessRunner runner,
        SandboxedProcessCommand command,
        SandboxConfigOverride? configOverride = null,
        SandboxedProcessOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);

        await using var handle = await runner.StartAsync(
            command,
            configOverride,
            options,
            cancellationToken).ConfigureAwait(false);

        return await handle.Completion.ConfigureAwait(false);
    }
}
