namespace HPD.Agent.Sandbox.Local;

using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using HPD.Agent.Sandbox;
using HPD.Agent.Sandbox.Events;
using HPD.Agent.Sandbox.Platforms;
using HPD.Agent.Sandbox.Local.Events;
using HPD.Agent.Sandbox.ProcessIsolation;
using Microsoft.Extensions.Logging;

/// <summary>
/// Publishes local HPD Execution providers for command execution and isolation.
/// </summary>
public sealed class LocalSandboxMiddleware : IAgentMiddleware, IAsyncDisposable
{
    private readonly ILogger<LocalSandboxMiddleware>? _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private SandboxIsolationManager? _processIsolationManager;
    private LocalProcessProvider? _processProvider;
    private EnvironmentProviderRegistry? _registry;
    private IEnvironmentRuntime? _runtime;
    private bool _initialized;

    public LocalSandboxMiddleware(ILogger<LocalSandboxMiddleware>? logger = null)
    {
        _logger = logger;
    }

    public bool IsInitialized => _initialized;

    public EnvironmentProviderRegistry? Registry => _registry;

    public IEnvironmentRuntime? Runtime => _runtime;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    public async Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        PublishCapabilities(context.RuntimeCapabilities);
        context.RegisterAsyncDisposable(this);
        await context.PublishAsync(new ProcessIsolationInitializedEvent
        {
            Platform = PlatformDetector.Current.ToString()
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (context.RuntimeCapabilities.TryGet<IEnvironmentRuntime>(out _))
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

            _processIsolationManager = new SandboxIsolationManager(_logger);
            _processProvider = new LocalProcessProvider(
                new SandboxIsolationPlanner(),
                new HostSandboxApplicator(_processIsolationManager));
            _registry = new EnvironmentProviderRegistry();
            _registry.RegisterModule(new LocalProcessProviderModule(_processProvider));
            _runtime = new InMemoryEnvironmentRuntime(_registry);
            _initialized = true;
            _logger?.LogInformation("Local HPD environment providers initialized.");
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
        SandboxIsolationManager? manager;

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
