using HPD.Agent.Middleware;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal sealed class CodingDebugResource : IAgentMiddleware, IAsyncDisposable
{
    private readonly Lazy<ServiceProvider> _services = new(() => new ServiceCollection()
        .AddHPDCodingDebugging()
        .AddHPDBuiltInDebugAdapters()
        .BuildServiceProvider());

    private readonly Lazy<DebugSessions> _directSessions = new(() => new());

    public DebugOperationDispatcher Dispatcher => _services.Value.GetRequiredService<DebugOperationDispatcher>();

    internal DebugSessions GetSessions(IRuntimeCapabilityRegistry capabilities) =>
        capabilities.TryGet<DebugSessions>(out var sessions) ? sessions : _directSessions.Value;

    public Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        context.RuntimeCapabilities.Set(new DebugSessions());
        return Task.CompletedTask;
    }

    public async Task AfterStoppedAsync(AfterStoppedContext context, CancellationToken cancellationToken)
    {
        if (context.RuntimeCapabilities.TryGet<DebugSessions>(out var sessions))
            await sessions.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_directSessions.IsValueCreated)
                await _directSessions.Value.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            if (_services.IsValueCreated)
                await _services.Value.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal sealed class DebugSessions : IAsyncDisposable
    {
        private readonly Lazy<DebugSessionManager> _manager = new(() => new(new DebugTerminalRecordStore(new())));

        public IDebugSessionManager Manager => _manager.Value;
        public DebugRuntimeBindingState State { get; } = new();

        public async ValueTask DisposeAsync()
        {
            State.Invalidate("AGENT_RUNTIME_STOPPED");
            if (_manager.IsValueCreated)
                await _manager.Value.DisposeAsync().ConfigureAwait(false);
        }
    }
}
