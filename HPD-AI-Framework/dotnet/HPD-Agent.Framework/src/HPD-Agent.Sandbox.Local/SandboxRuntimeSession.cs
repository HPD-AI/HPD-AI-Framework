using HPD.Agent.Sandbox;
using HPD.Agent;
using Microsoft.Extensions.Logging;

namespace HPD.Sandbox.Local;

internal sealed class SandboxRuntimeSession : IAsyncDisposable
{
    private readonly LocalSandboxedProcessRunner _runner;
    private bool _started;
    private bool _disposed;

    public SandboxRuntimeSession(
        SandboxConfig globalConfig,
        ILogger? logger = null,
        Action<AgentEvent>? eventSink = null)
    {
        GlobalConfig = globalConfig ?? throw new ArgumentNullException(nameof(globalConfig));
        _runner = new LocalSandboxedProcessRunner(GlobalConfig, logger: logger, eventSink: eventSink);
        ProcessRunner = _runner;
    }

    public SandboxConfig GlobalConfig { get; }

    public ISandboxedProcessRunner ProcessRunner { get; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _started = true;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_started)
            return;

        await DisposeAsync();
        _started = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _runner.DisposeAsync();
    }
}
