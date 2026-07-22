using HPD.Agent.Middleware;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

/// <summary>
/// Owns and publishes debugger services whose lifetime is exactly one agent runtime.
/// A host attaches one middleware instance to each Coding agent runtime.
/// </summary>
public sealed class DebugRuntimeAttachmentMiddleware : IAgentMiddleware, IAsyncDisposable
{
    private readonly DebugSessionManager _sessionManager = new();
    private readonly DebugRuntimeBindingState _bindingState = new();
    private readonly IDisposable _invalidationRegistration;
    private int _disposed;

    public DebugRuntimeAttachmentMiddleware()
        => _invalidationRegistration = _bindingState.OnInvalidated(reason => { _ = _sessionManager.DisposeAsync(); });

    public Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(DebugRuntimeAttachmentMiddleware));
        context.RuntimeCapabilities.Set<IDebugSessionManager>(_sessionManager);
        context.RuntimeCapabilities.Set(_bindingState);
        return Task.CompletedTask;
    }

    public async Task BeforeStopAsync(BeforeStopContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _bindingState.Invalidate("AGENT_RUNTIME_STOPPED");
        _invalidationRegistration.Dispose();
        await _sessionManager.DisposeAsync().ConfigureAwait(false);
    }
}
