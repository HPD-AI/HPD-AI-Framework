namespace HPD.Execution.Local;

using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Execution.Contracts;
using HPD.Execution.Runtime;
using HPD.Execution.Local.Events;
using HPD.Execution.Local.ProcessIsolation;
using Microsoft.Extensions.Logging;

/// <summary>
/// Publishes local HPD Execution providers for command execution and isolation.
/// </summary>
public sealed class LocalExecutionMiddleware : IAgentMiddleware, IAsyncDisposable
{
    private readonly ILogger<LocalExecutionMiddleware>? _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private LocalProcessIsolationManager? _processIsolationManager;
    private LocalProcessProvider? _processProvider;
    private ExecutionProviderRegistry? _registry;
    private IExecutionRuntime? _runtime;
    private bool _initialized;

    public LocalExecutionMiddleware(ILogger<LocalExecutionMiddleware>? logger = null)
    {
        _logger = logger;
    }

    public bool IsInitialized => _initialized;

    public ExecutionProviderRegistry? Registry => _registry;

    public IExecutionRuntime? Runtime => _runtime;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    public async Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        PublishCapabilities(context.RuntimeCapabilities);
        context.RegisterAsyncDisposable(this);
        context.Emit(new ProcessIsolationInitializedEvent
        {
            Platform = Platforms.PlatformDetector.Current.ToString()
        });
    }

    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (context.RuntimeCapabilities.TryGet<IExecutionRuntime>(out _))
            return;

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        PublishCapabilities(context.RuntimeCapabilities);
    }

    public async Task BeforeStopAsync(BeforeStopContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisposeRuntimeAsync().ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            _processIsolationManager = new LocalProcessIsolationManager(_logger);
            _processProvider = new LocalProcessProvider();
            _registry = new ExecutionProviderRegistry();
            _registry.RegisterLocalProcessIsolation(_processIsolationManager);
            _registry.RegisterModule(new LocalProcessProviderModule(_processProvider));
            _runtime = new InMemoryExecutionRuntime(_registry);
            _initialized = true;
            _logger?.LogInformation("Local HPD execution providers initialized.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void PublishCapabilities(IRuntimeCapabilityRegistry capabilities)
    {
        capabilities.Set(_registry!);
        capabilities.Set(_runtime!);
        capabilities.Set<IProcessProvider>(_processProvider!);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeRuntimeAsync().ConfigureAwait(false);
        _initLock.Dispose();
    }

    private async Task DisposeRuntimeAsync()
    {
        LocalProcessIsolationManager? manager;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            manager = _processIsolationManager;
            _processProvider = null;
            _processIsolationManager = null;
            _registry = null;
            _runtime = null;
            _initialized = false;
        }
        finally
        {
            _initLock.Release();
        }

        if (manager is not null)
            await manager.DisposeAsync().ConfigureAwait(false);
    }
}
