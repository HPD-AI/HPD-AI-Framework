namespace HPD.Agent.Sandbox;

using HPD.Agent.Middleware;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

/// <summary>Publishes matching workspace and execution bindings from one Environment session.</summary>
public sealed class EnvironmentSandboxMiddleware(
    ISandboxProvisioningProvider provider,
    SandboxSessionRequest? request,
    Func<AgentWorkspaceReference, CancellationToken, ValueTask<SandboxSessionRequest>>? resolveWorkspace = null)
    : IAgentOwnedMiddleware
{
    private readonly ISandboxProvisioningProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SandboxSessionRequest, ActiveSession> _sessions = new(new SessionRequestComparer());
    private ActiveSession? _current;
    private bool _disposed;

    public bool IsInitialized => _current is not null;
    public ISandboxSession? Session => _current?.Session;
    public EnvironmentProviderRegistry? Registry => _current?.Factory.Registry;
    public IEnvironmentRuntime? Runtime => _current?.Factory.Runtime;

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        InitializeAsync(null, cancellationToken);

    public Task InitializeAsync(AgentRunConfig? runConfig, CancellationToken cancellationToken = default) =>
        BindAsync(runConfig, null, cancellationToken);

    public Task BeforeStartAsync(BeforeStartContext context, CancellationToken cancellationToken) =>
        request is null && context.RunConfig?.Workspace is null
            ? Task.CompletedTask
            : BindAsync(context.RunConfig, context.RuntimeCapabilities, cancellationToken);

    public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken) =>
        BindAsync(context.RunConfig, context.RuntimeCapabilities, cancellationToken);

    public Task AfterStoppedAsync(AfterStoppedContext context, CancellationToken cancellationToken) =>
        ReleaseAsync(dispose: false);

    public async ValueTask DisposeAsync() => await ReleaseAsync(dispose: true).ConfigureAwait(false);

    private async Task BindAsync(
        AgentRunConfig? runConfig,
        IRuntimeCapabilityRegistry? capabilities,
        CancellationToken cancellationToken)
    {
        SandboxSessionRequest? selected = request;
        if (runConfig?.Workspace is { } workspace)
        {
            if (resolveWorkspace is null || string.IsNullOrWhiteSpace(workspace.Id))
                throw new InvalidOperationException("The selected workspace cannot be resolved by this host.");

            selected = await resolveWorkspace(workspace, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The selected workspace is unavailable.");
        }

        if (selected is null)
            throw new InvalidOperationException("Select a workspace for this run.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sessions.TryGetValue(selected, out var active))
            {
                active = await CreateSessionAsync(selected, cancellationToken).ConfigureAwait(false);
                _sessions.Add(selected, active);
            }
            _current = active;
            if (capabilities is not null)
            {
                capabilities.Set(active.Factory.Registry!);
                capabilities.Set(active.Factory.Runtime!);
                capabilities.Set(active.Session);
                capabilities.Set(active.Session.Execution);
                capabilities.Set(active.Session.Workspace);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ActiveSession> CreateSessionAsync(
        SandboxSessionRequest selected, CancellationToken cancellationToken)
    {
        var module = _provider.CreateSessionProvider();
        EnvironmentSandboxBackendFactory? factory = null;
        ISandboxSession? session = null;
        try
        {
            var plan = module.PrepareSandbox(new SandboxProvisioningRequest
            {
                Isolation = selected.Isolation,
                Workspaces = selected.Workspaces,
                Resources = selected.Resources,
                Lifecycle = selected.Lifecycle,
            }, cancellationToken);
            factory = EnvironmentSandboxBackendFactory.Compose(module, plan);
            session = await factory.CreateSessionAsync(
                selected with
                {
                    Workspaces = plan.Workspaces ?? selected.Workspaces,
                    Isolation = plan.Isolation ?? selected.Isolation,
                },
                cancellationToken).ConfigureAwait(false);
            if (plan.InitializeWorkspace is { } initialize)
                await initialize(session.Workspace.Files, cancellationToken).ConfigureAwait(false);
            return new ActiveSession(factory, plan, session);
        }
        catch
        {
            try
            {
                if (session is not null)
                    await session.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                if (factory is not null)
                    await factory.DisposeAsync().ConfigureAwait(false);
                else if (module is IAsyncDisposable disposable)
                    await disposable.DisposeAsync().ConfigureAwait(false);
            }
            throw;
        }
    }

    private async Task ReleaseAsync(bool dispose)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed |= dispose;
            _current = null;
            List<Exception> failures = [];
            foreach (var active in _sessions.Values)
            {
                try
                {
                    if (active.Plan.FinalizeWorkspace is { } finalize)
                        await finalize(active.Session.Workspace.Files).ConfigureAwait(false);
                }
                catch (Exception error) { failures.Add(error); }
                try { await active.Session.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
                try { await active.Factory.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            }
            _sessions.Clear();
            if (failures.Count > 0)
                throw new AggregateException("Sandbox session cleanup did not complete.", failures);
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class SessionRequestComparer : IEqualityComparer<SandboxSessionRequest>
    {
        public bool Equals(SandboxSessionRequest? left, SandboxSessionRequest? right) =>
            ReferenceEquals(left, right) ||
            (left is not null && right is not null &&
                WithoutWorkspaces(left) == WithoutWorkspaces(right) &&
                left.Workspaces.SequenceEqual(right.Workspaces));

        public int GetHashCode(SandboxSessionRequest request)
        {
            HashCode hash = new();
            hash.Add(WithoutWorkspaces(request));
            foreach (var workspace in request.Workspaces)
                hash.Add(workspace);
            return hash.ToHashCode();
        }

        private static SandboxSessionRequest WithoutWorkspaces(SandboxSessionRequest request) =>
            request with { Workspaces = Array.Empty<SandboxWorkspaceRequest>() };
    }

    private sealed record ActiveSession(
        EnvironmentSandboxBackendFactory Factory,
        SandboxProvisioningPlan Plan,
        ISandboxSession Session);
}
