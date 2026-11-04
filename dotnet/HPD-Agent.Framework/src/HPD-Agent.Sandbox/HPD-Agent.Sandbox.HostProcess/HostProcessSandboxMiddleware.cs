namespace HPD.Agent.Sandbox.HostProcess;

using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Sandbox;
using HPD.Agent.Sandbox.HostProcess.Events;
using HPD.Environment.Contracts;
using HPD.Environment.HostProcess;
using HPD.Environment.Runtime;
using Microsoft.Extensions.Logging;

/// <summary>
/// Adapts the HPD Host Process Environment backend into the Agent runtime.
/// </summary>
public sealed class HostProcessSandboxMiddleware
    : IAgentMiddleware, IAsyncDisposable
{
    private readonly ILogger<HostProcessSandboxMiddleware>? _logger;
    private readonly HostProcessSandboxOptions _options;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private EnvironmentSandboxBackendFactory? _factory;
    private ISandboxSession? _session;
    private string? _workspaceSelection;
    private int _disposed;
    private bool _initialized;

    public HostProcessSandboxMiddleware(
        ILogger<HostProcessSandboxMiddleware>? logger = null,
        HostProcessSandboxOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new HostProcessSandboxOptions();
    }

    public bool IsInitialized => _initialized;

    public EnvironmentProviderRegistry? Registry => _factory?.Registry;

    public IEnvironmentRuntime? Runtime => _factory?.Runtime;

    public ISandboxSession? Session => _session;

    internal RuntimeProcessExecutionBinding? ExecutionBinding =>
        _session?.Execution;

    public Task InitializeAsync(
        CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(null, cancellationToken);

    /// <summary>
    /// Initializes the sandbox from a specific run configuration.
    /// </summary>
    public Task InitializeAsync(
        AgentRunConfig? runConfig,
        CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(runConfig, cancellationToken);

    public async Task BeforeStartAsync(
        BeforeStartContext context,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(context.RunConfig, cancellationToken)
            .ConfigureAwait(false);
        PublishCapabilities(context.RuntimeCapabilities);
        context.RegisterAsyncDisposable(this);
        PlatformSpec platform =
            HostProcessProviderDescriptor.CurrentPlatform();
        await context.PublishAsync(
            new ProcessIsolationInitializedEvent
            {
                Platform =
                    $"{platform.OperatingSystem}/{platform.Architecture}",
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(context.RunConfig, cancellationToken)
            .ConfigureAwait(false);
        if (context.RuntimeCapabilities.TryGet<ISandboxSession>(out _))
        {
            return;
        }

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
        AgentRunConfig? runConfig,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        if (_initialized)
        {
            EnsureSameWorkspace(runConfig);
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
                EnsureSameWorkspace(runConfig);
                return;
            }

            IReadOnlyList<HostProcessWorkspaceOptions> workspaces =
                ResolveWorkspaces(runConfig);
            var module = new HostProcessProviderModule();
            _factory = new EnvironmentSandboxBackendFactory(
                new EnvironmentSandboxBackendDefinition
                {
                    Kind = SandboxBackendKind.HostProcess,
                    Module = module,
                    EnforcementLocation = SandboxEnforcementLocation.Host,
                    HostSpecFactory = request =>
                        new RuntimeHostSpec
                        {
                            PreferredProvider =
                                HostProcessProviderDescriptor.ProviderId,
                            Platform =
                                HostProcessProviderDescriptor.CurrentPlatform(),
                            Capacity = request.Resources,
                            LifecyclePolicy = request.Lifecycle,
                        },
                });
            _session = await _factory.CreateSessionAsync(
                CreateSessionRequest(workspaces),
                cancellationToken).ConfigureAwait(false);
            _workspaceSelection = GetWorkspaceSelection(workspaces);
            _initialized = true;
            _logger?.LogInformation(
                "Host Process sandbox session initialized.");
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

    private SandboxSessionRequest CreateSessionRequest(
        IReadOnlyList<HostProcessWorkspaceOptions> workspaces) =>
        new()
        {
            Isolation = _options.Isolation,
            Workspaces = workspaces
                .Select(CreateWorkspaceRequest)
                .ToArray(),
        };

    private IReadOnlyList<HostProcessWorkspaceOptions> ResolveWorkspaces(
        AgentRunConfig? runConfig) =>
        _options.WorkspaceOptionsFactory?.Invoke(runConfig)
            ?? _options.Workspaces;

    private void EnsureSameWorkspace(AgentRunConfig? runConfig)
    {
        if (_options.WorkspaceOptionsFactory is null)
        {
            return;
        }

        string requested = GetWorkspaceSelection(ResolveWorkspaces(runConfig));
        if (!string.Equals(
                requested,
                _workspaceSelection,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The workspace selected by this run differs from the active sandbox workspace. Start a new session or thread before switching workspaces.");
        }
    }

    private static string GetWorkspaceSelection(
        IReadOnlyList<HostProcessWorkspaceOptions> workspaces) =>
        string.Join(
            '\n',
            workspaces
                .OrderBy(
                    static workspace => workspace.RootId,
                    StringComparer.Ordinal)
                .Select(workspace =>
                    $"{workspace.RootId}\t{Path.GetFullPath(workspace.Path)}\t{(int)workspace.Access}"));

    private static SandboxWorkspaceRequest CreateWorkspaceRequest(
        HostProcessWorkspaceOptions workspace)
    {
        string rootPath = Path.GetFullPath(workspace.Path);
        var rootId = new WorkspaceRootId(workspace.RootId);
        var access = workspace.Access;
        return new SandboxWorkspaceRequest
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.HostPath,
                HostPath = new HostPathSelection(
                    new HostPath(rootPath),
                    HostPathKind.Directory),
            },
            Root = new WorkspaceRootBinding
            {
                Id = rootId,
                DisplayName = workspace.DisplayName ??
                    Path.GetFileName(
                        Path.TrimEndingDirectorySeparator(rootPath)),
                Access = access,
                DefaultExecutionPath =
                    new WorkspacePath(rootId, string.Empty),
                Content = new WorkspaceContentIdentity(
                    $"host-process:{rootPath}"),
                PathSemantics = new WorkspacePathSemantics(
                    CaseSensitive: OperatingSystem.IsLinux(),
                    Symlinks:
                        SymlinkEvaluationPolicy.DenySymlinks),
            },
            ExecutionPath =
                new WorkspaceExecutionPath(rootPath),
            Role = ContentProjectionRole.Workspace,
            SyncPolicy = SyncPolicy.None,
            FinalizationPolicy = FinalizationPolicy.Explicit,
            SecurityPolicy =
                ContentProjectionSecurityPolicy.Default with
                {
                    AllowHostPathSource = true,
                    AllowDirectSourceMutation =
                        access is not AccessMode.ReadOnly,
                    ReadOnlyEnforcement =
                        ReadOnlyEnforcementPolicy.BestEffort,
                },
        };
    }
}
