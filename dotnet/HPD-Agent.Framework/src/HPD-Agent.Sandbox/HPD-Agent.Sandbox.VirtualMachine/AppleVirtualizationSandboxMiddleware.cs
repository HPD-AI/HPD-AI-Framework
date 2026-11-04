namespace HPD.Agent.Sandbox.VirtualMachine;

using System.Runtime.InteropServices;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using HPD.Environment.Virtualization.Apple;
using Microsoft.Extensions.Logging;

/// <summary>
/// Adapts the Apple Virtualization Environment backend into the Agent runtime.
/// Environment lifecycle composition remains backend-neutral.
/// </summary>
public sealed class AppleVirtualizationSandboxMiddleware
    : IAgentMiddleware, IAsyncDisposable
{
    private readonly AppleVirtualizationProviderOptions _options;
    private readonly ILogger<AppleVirtualizationSandboxMiddleware>? _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private EnvironmentSandboxBackendFactory? _factory;
    private ISandboxSession? _session;
    private int _disposed;
    private bool _initialized;

    public AppleVirtualizationSandboxMiddleware(
        AppleVirtualizationProviderOptions? options = null,
        ILogger<AppleVirtualizationSandboxMiddleware>? logger = null)
    {
        _options = options ?? new AppleVirtualizationProviderOptions();
        _logger = logger;
    }

    public bool IsInitialized => _initialized;

    public EnvironmentProviderRegistry? Registry => _factory?.Registry;

    public IEnvironmentRuntime? Runtime => _factory?.Runtime;

    public ISandboxSession? Session => _session;

    public Task InitializeAsync(
        CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(cancellationToken);

    /// <summary>
    /// Creates one VM-backed sandbox session after the caller has supplied its
    /// complete sandbox and workspace request.
    /// </summary>
    public async ValueTask<ISandboxSession> CreateSessionAsync(
        SandboxSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);
        if (_session is not null)
        {
            throw new InvalidOperationException(
                "The Apple Virtualization sandbox middleware already owns a session.");
        }

        _session = await _factory!.CreateSessionAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        return _session;
    }

    public async Task BeforeStartAsync(
        BeforeStartContext context,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);
        if (_session is null)
        {
            _session = await _factory!.CreateSessionAsync(
                new SandboxSessionRequest
                {
                    Isolation = AppleSandboxIsolation(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        PublishCapabilities(context.RuntimeCapabilities);
        context.RegisterAsyncDisposable(this);
    }

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        if (context.RuntimeCapabilities.TryGet<ISandboxSession>(out _))
        {
            return;
        }

        await EnsureInitializedAsync(cancellationToken)
            .ConfigureAwait(false);
        PublishCapabilities(context.RuntimeCapabilities);
    }

    public async Task BeforeStopAsync(
        BeforeStopContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await DisposeRuntimeAsync().ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            if (_initialized)
            {
                return;
            }

            var module = new AppleVirtualizationProviderModule(_options);
            _factory = new EnvironmentSandboxBackendFactory(
                new EnvironmentSandboxBackendDefinition
                {
                    Kind = SandboxBackendKind.VirtualMachine,
                    Module = module,
                    EnforcementLocation =
                        SandboxEnforcementLocation.Guest,
                    HostSpecFactory = CreateHostSpec,
                    ActivationSpecFactory = _ => CreateActivationSpec(),
                });
            await _factory.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            _initialized = true;
            _logger?.LogInformation(
                "Apple Virtualization sandbox session initialized.");
        }
        catch
        {
            if (_factory is not null)
            {
                await _factory.DisposeAsync().ConfigureAwait(false);
                _factory = null;
            }

            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private RuntimeHostSpec CreateHostSpec(
        SandboxSessionRequest request) =>
        new()
        {
            PreferredProvider =
                AppleVirtualizationProviderDescriptor.ProviderId,
            Platform = GuestPlatform(),
            Capacity = request.Resources with
            {
                CpuCores = request.Resources.CpuCores ??
                    _options.DefaultCpuCores,
                MemoryBytes = request.Resources.MemoryBytes ??
                    _options.DefaultMemoryBytes,
                StorageBytes = request.Resources.StorageBytes ??
                    _options.DefaultDiskBytes,
            },
            LifecyclePolicy = request.Lifecycle,
            Bootstrap = new RuntimeHostBootstrapSpec
            {
                GuestComponents =
                [
                    new GuestComponentSpec(
                        GuestComponentKind.GuestAgent,
                        "hpd-guest-agent"),
                ],
                ReadinessGates =
                [
                    new ReadinessGateSpec(
                        "guest-agent-handshake",
                        ReadinessGateKind.GuestControlReachable,
                        ReadinessGateScope.GuestControl,
                        new RetryPolicy(
                            MaxAttempts: 30,
                            Delay: TimeSpan.FromSeconds(1)),
                        Timeout: TimeSpan.FromSeconds(30)),
                ],
            },
            TopologyPolicy = new RuntimeTopologyPolicy
            {
                Mode = RuntimeTopologyMode.OneHostPerRuntime,
            },
        };

    private static ProcessIsolationPolicy AppleSandboxIsolation() =>
        ProcessIsolationPolicy.Default with
        {
            Network = new NetworkEgressPolicy
            {
                Mode = NetworkEgressMode.Unrestricted,
                RequireProxyMediation = false,
            },
            Interactive = ProcessInteractivePolicy.Default with
            {
                AllowStdin = true,
                AllowPty = false,
            },
            Violations = ProcessViolationPolicy.Default with
            {
                Action = ProcessViolationAction.ObserveOnly,
            },
        };

    private PlatformSpec GuestPlatform()
    {
        string architecture =
            _options.GuestImage.Architecture switch
            {
                AppleVirtualizationGuestArchitectureExpectation.Arm64 =>
                    "arm64",
                AppleVirtualizationGuestArchitectureExpectation.X64 =>
                    "x64",
                _ => RuntimeInformation.ProcessArchitecture ==
                    Architecture.Arm64
                        ? "arm64"
                        : "x64",
            };
        return new PlatformSpec("linux", architecture);
    }

    private static ProviderActivationSpec CreateActivationSpec() =>
        new()
        {
            ProviderId =
                AppleVirtualizationProviderDescriptor.ProviderId,
            Scope = ProviderActivationScope.Runtime,
            ScopeKey = "agent-sandbox-apple-virtualization",
            RequiredContracts =
                AppleVirtualizationProviderDescriptor.FirstSliceContracts,
            ActivationKind =
                ProviderActivationKind.SupervisedExecutable,
            Supervisor = new ProviderSupervisorRequirement(
                true,
                RestartOnFailure: false,
                TimeSpan.FromSeconds(5)),
            Transport = new ProviderTransportRequirement(
                ProviderTransportKind.StdIo,
                RequiresStreaming: true,
                RequiresHandlePassing: false,
                RequiresPeerAuthentication: false),
            AuthPolicy = new ProviderAuthPolicy(
                "current-user",
                RequireSameUser: true,
                AllowRemoteIdentity: false),
            HealthPolicy = new ProviderHealthPolicy(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(2)),
            LogPolicy = new ProviderLogPolicy(
                "memory",
                CaptureStartupLogs: true,
                CaptureDiagnosticLogs: true),
        };

    private void PublishCapabilities(
        IRuntimeCapabilityRegistry capabilities)
    {
        capabilities.Set(_factory!.Registry!);
        capabilities.Set(_factory.Runtime!);
        capabilities.Set(_session!);
        capabilities.Set(_session!.Execution);
        capabilities.Set(_session.Workspace);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        await DisposeRuntimeAsync().ConfigureAwait(false);
    }

    private async Task DisposeRuntimeAsync()
    {
        ISandboxSession? session;
        EnvironmentSandboxBackendFactory? factory;

        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            session = _session;
            factory = _factory;
            _session = null;
            _factory = null;
            _initialized = false;
        }
        finally
        {
            _initLock.Release();
        }

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        if (factory is not null)
        {
            await factory.DisposeAsync().ConfigureAwait(false);
        }
    }
}
