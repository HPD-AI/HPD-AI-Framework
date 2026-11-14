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
    private readonly Dictionary<string, (EnvironmentSandboxBackendFactory Factory, ISandboxSession Session)> _bindings = new(StringComparer.Ordinal);
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
        await EnsureInitializedAsync(context.RunConfig, cancellationToken, context.RuntimeCapabilities)
            .ConfigureAwait(false);
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
        await EnsureInitializedAsync(context.RunConfig, cancellationToken, context.RuntimeCapabilities)
            .ConfigureAwait(false);
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
        CancellationToken cancellationToken,
        IRuntimeCapabilityRegistry? capabilities = null)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        await _initLock.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);


            IReadOnlyList<HostProcessWorkspaceOptions> workspaces =
                ResolveWorkspaces(runConfig);
            string selection = GetWorkspaceSelection(workspaces);
            if (_bindings.TryGetValue(selection, out var existing))
            {
                (_factory, _session) = existing;
                if (capabilities is not null)
                    PublishCapabilities(capabilities);
                return;
            }

            var module = new HostProcessProviderModule();
            bool liveWorkspace = OperatingSystem.IsWindows() && workspaces.Count > 0 &&
                workspaces.All(workspace => workspace.Access is AccessMode.ReadWrite or AccessMode.ReadOnly) &&
                _options.Isolation.Filesystem.Rules.Count == 0 &&
                _options.Isolation.Network.Mode == NetworkEgressMode.Blocked &&
                _options.Isolation.Mode != ProcessIsolationMode.Disabled;
            var factory = new EnvironmentSandboxBackendFactory(
                new EnvironmentSandboxBackendDefinition
                {
                    Kind = SandboxBackendKind.HostProcess,
                    Module = module,
                    EnforcementLocation = SandboxEnforcementLocation.Host,
                    RefineCapabilities = (query, report) =>
                        HostProcessProviderModule.RefineSandboxCapabilities(
                            report, query.Isolation, query.Workspaces.Count != 0 && !liveWorkspace),
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
            ISandboxSession session;
            try
            {
                if (liveWorkspace && OperatingSystem.IsWindows())
                    module.BindWindowsWorkspaces(
                        workspaces.Select(workspace => (workspace.Path, workspace.Access)).ToArray(), cancellationToken);
                session = await factory.CreateSessionAsync(
                    CreateSessionRequest(workspaces), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await factory.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            _bindings.Add(selection, (factory, session));
            (_factory, _session) = (factory, session);
            _initialized = true;
            if (capabilities is not null)
                PublishCapabilities(capabilities);
            _logger?.LogInformation("Host Process sandbox workspace activated.");
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
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            _session = null;
            _factory = null;
            _initialized = false;

            List<Exception> errors = [];
            foreach (var (factory, session) in _bindings.Values)
            {
                try { await session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { errors.Add(error); }
                try { await factory.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { errors.Add(error); }
            }
            _bindings.Clear();
            if (errors.Count > 0)
                throw new AggregateException("Could not release all sandbox workspace bindings.", errors);
        }
        finally
        {
            _initLock.Release();
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

    private static string GetWorkspaceSelection(
        IReadOnlyList<HostProcessWorkspaceOptions> workspaces) =>
        string.Concat(
            workspaces
                .OrderBy(
                    static workspace => workspace.RootId,
                    StringComparer.Ordinal)
                .Select(workspace => string.Concat(
                    EncodeSelectionPart(workspace.RootId),
                    EncodeSelectionPart(Path.GetFullPath(workspace.Path)),
                    EncodeSelectionPart(((int)workspace.Access).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    EncodeSelectionPart(workspace.DisplayName))));

    private static string EncodeSelectionPart(string? value) =>
        value is null ? "-1:" : FormattableString.Invariant($"{value.Length}:{value}");

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
