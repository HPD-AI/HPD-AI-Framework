namespace HPD.Agent.Sandbox.AppleVirtualization;

using HPD.Agent.Middleware;
using HPD.Environment.AppleVirtualization;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Microsoft.Extensions.Logging;

/// <summary>
/// Publishes Apple Virtualization environment providers for sandbox-aware agent workloads.
/// </summary>
public sealed class AppleVirtualizationSandboxMiddleware : IAgentMiddleware, IAsyncDisposable
{
    private readonly AppleVirtualizationProviderOptions _options;
    private readonly ILogger<AppleVirtualizationSandboxMiddleware>? _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private AppleVirtualizationProviderModule? _module;
    private EnvironmentProviderRegistry? _registry;
    private IEnvironmentRuntime? _runtime;
    private readonly List<(IProviderActivator Activator, TargetHandle<ProviderActivation> Handle)> _activations = [];
    private bool _initialized;

    public AppleVirtualizationSandboxMiddleware(
        AppleVirtualizationProviderOptions? options = null,
        ILogger<AppleVirtualizationSandboxMiddleware>? logger = null)
    {
        _options = options ?? new AppleVirtualizationProviderOptions();
        _logger = logger;
    }

    public bool IsInitialized => _initialized;

    public AppleVirtualizationProviderModule? Module => _module;

    public EnvironmentProviderRegistry? Registry => _registry;

    public IEnvironmentRuntime? Runtime => _runtime;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    public async Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        PublishCapabilities(context.RuntimeCapabilities);
        context.RegisterAsyncDisposable(this);
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

            _module = new AppleVirtualizationProviderModule(_options);
            _registry = new EnvironmentProviderRegistry();
            _registry.RegisterModule(_module);
            await ActivateProvidersAsync(_registry, cancellationToken).ConfigureAwait(false);
            _runtime = new InMemoryEnvironmentRuntime(_registry);
            _initialized = true;
            _logger?.LogInformation("Apple Virtualization HPD environment providers initialized for agent sandbox use.");
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
    }

    private async Task ActivateProvidersAsync(
        EnvironmentProviderRegistry registry,
        CancellationToken cancellationToken)
    {
        foreach (IProviderActivator activator in registry.ProviderActivators)
        {
            ResourceSnapshot<ProviderActivation, ProviderActivationSpec, ProviderActivationStatus> activation =
                await activator.ActivateAsync(ActivationSpec(), cancellationToken).ConfigureAwait(false);

            if (activation.Status.ActivationPhase != ProviderActivationPhase.Ready ||
                activation.Status.ActivationHandle is null)
            {
                throw new InvalidOperationException(
                    "Apple Virtualization provider activation failed. " +
                    string.Join(" | ", activation.Status.Diagnostics.Select(diagnostic =>
                        diagnostic.Code.Value + ": " + diagnostic.Message)));
            }

            _activations.Add((activator, activation.Status.ActivationHandle.Value));
        }
    }

    private static ProviderActivationSpec ActivationSpec() =>
        new()
        {
            ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
            Scope = ProviderActivationScope.Runtime,
            ScopeKey = "agent-sandbox-apple-virtualization",
            RequiredContracts = AppleVirtualizationProviderDescriptor.FirstSliceContracts,
            ActivationKind = ProviderActivationKind.SupervisedExecutable,
            Supervisor = new ProviderSupervisorRequirement(true, RestartOnFailure: false, TimeSpan.FromSeconds(5)),
            Transport = new ProviderTransportRequirement(
                ProviderTransportKind.StdIo,
                RequiresStreaming: true,
                RequiresHandlePassing: false,
                RequiresPeerAuthentication: false),
            AuthPolicy = new ProviderAuthPolicy("current-user", RequireSameUser: true, AllowRemoteIdentity: false),
            HealthPolicy = new ProviderHealthPolicy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)),
            LogPolicy = new ProviderLogPolicy("memory", CaptureStartupLogs: true, CaptureDiagnosticLogs: true),
        };

    public async ValueTask DisposeAsync()
    {
        await DisposeRuntimeAsync().ConfigureAwait(false);
        _initLock.Dispose();
    }

    private async Task DisposeRuntimeAsync()
    {
        List<(IProviderActivator Activator, TargetHandle<ProviderActivation> Handle)> activations;
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            activations = [.. _activations];
            _activations.Clear();
            _module = null;
            _registry = null;
            _runtime = null;
            _initialized = false;
        }
        finally
        {
            _initLock.Release();
        }

        foreach ((IProviderActivator activator, TargetHandle<ProviderActivation> handle) in activations)
        {
            await activator.StopAsync(
                handle,
                new ProviderStopOptions(TimeSpan.FromSeconds(2), Force: true, Reason: "agent sandbox middleware dispose"))
                .ConfigureAwait(false);
        }
    }
}
