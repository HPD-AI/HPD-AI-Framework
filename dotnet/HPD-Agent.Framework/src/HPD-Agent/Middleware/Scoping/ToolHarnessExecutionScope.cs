using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Middleware;

internal enum ToolHarnessPipelineLifecycleState { Activating, Active, Deactivating, Disposed }

internal sealed record ToolHarnessRegistryEntryDiagnostics(
    string HarnessIdentity,
    long ActivationOrdinal,
    ToolHarnessPipelineLifecycleState State,
    int AdmissionCount);

internal sealed record ToolHarnessRegistryDiagnostics(
    IReadOnlyList<ToolHarnessRegistryEntryDiagnostics> Entries);

internal sealed class ToolHarnessExecutionScope
{
    private readonly AsyncServiceScope? _serviceScope;
    private readonly ToolHarnessPipelineRegistry _registry;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _owners = 1;
    private int _teardownStarted;
    private ToolHarnessDeactivationReason _reason = ToolHarnessDeactivationReason.Completed;
    private readonly Action<Exception>? _cleanupObserver;

    private ToolHarnessExecutionScope(IServiceProvider? baseServices, Action<Exception>? cleanupObserver)
    {
        _cleanupObserver = cleanupObserver;
        CanonicalWorkspaceIdentity = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(global::System.Environment.CurrentDirectory));
        var factory = baseServices?.GetService<IServiceScopeFactory>();
        _serviceScope = factory is null ? null : factory.CreateAsyncScope();
        Services = _serviceScope?.ServiceProvider;
        _registry = new ToolHarnessPipelineRegistry();
    }

    internal static ToolHarnessExecutionScope Create(IServiceProvider? baseServices, Action<Exception>? cleanupObserver = null) =>
        new(baseServices, cleanupObserver);
    internal IServiceProvider? Services { get; }
    internal string CanonicalWorkspaceIdentity { get; }
    internal ToolHarnessPipelineRegistry Registry => _registry;
    internal Task Completion => _completion.Task;

    internal ToolHarnessExecutionLease TransferToOperation(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        while (true)
        {
            var current = Volatile.Read(ref _owners);
            if (current <= 0 || Volatile.Read(ref _teardownStarted) != 0)
                throw new InvalidOperationException("The ToolHarness execution scope is closing and cannot transfer ownership.");
            if (Interlocked.CompareExchange(ref _owners, current + 1, current) == current)
                return new ToolHarnessExecutionLease(this, operationId);
        }
    }

    internal ValueTask ReleaseForegroundAsync(ToolHarnessDeactivationReason reason)
    {
        _reason = reason;
        return ReleaseOwnerAsync();
    }

    internal ValueTask ReleaseOwnerAsync()
    {
        var remaining = Interlocked.Decrement(ref _owners);
        if (remaining < 0)
            throw new InvalidOperationException("ToolHarness execution ownership was released more than once.");
        if (remaining != 0 || Interlocked.Exchange(ref _teardownStarted, 1) != 0)
            return ValueTask.CompletedTask;
        return new ValueTask(TeardownAsync());
    }

    private async Task TeardownAsync()
    {
        List<Exception>? failures = null;
        try
        {
            try { await _registry.DeactivateAllAsync(_reason).ConfigureAwait(false); }
            catch (Exception ex) { (failures ??= []).Add(ex); }

            if (_serviceScope is { } scope)
            {
                try { await scope.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }

            if (failures is { Count: > 0 })
                throw new AggregateException("ToolHarness execution cleanup failed.", failures);

            _completion.TrySetResult();
        }
        catch (Exception ex)
        {
            try { _cleanupObserver?.Invoke(ex); } catch { }
            _completion.TrySetException(ex);
            throw;
        }
    }
}

internal sealed class ToolHarnessExecutionLease(ToolHarnessExecutionScope owner, string operationId) : IAsyncDisposable
{
    private ToolHarnessExecutionScope? _owner = owner;
    internal string OperationId { get; } = operationId;
    internal ToolHarnessPipelineRegistry Registry => (_owner ?? throw new ObjectDisposedException(nameof(ToolHarnessExecutionLease))).Registry;

    public ValueTask DisposeAsync()
    {
        var current = Interlocked.Exchange(ref _owner, null);
        return current is null ? ValueTask.CompletedTask : current.ReleaseOwnerAsync();
    }
}

internal sealed class ToolHarnessPipelineRegistry
{
    private readonly ConcurrentDictionary<string, Lazy<Task<ActivatedToolHarnessPipeline>>> _entries =
        new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _activationOrdinal;
    private int _closing;

    internal async ValueTask<ToolHarnessPipelineLease> AcquireAsync(
        ToolHarnessFactory harness,
        ToolHarnessActivationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(context);
        var identity = harness.ActivationIdentity;
        Lazy<Task<ActivatedToolHarnessPipeline>> lazy;
        Task<ActivatedToolHarnessPipeline> activation;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing != 0, this);
            lazy = _entries.GetOrAdd(identity, _ => new Lazy<Task<ActivatedToolHarnessPipeline>>(
                () => ActivateAsync(harness, context, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
            // Start activation before releasing the close/admission gate so shutdown's
            // snapshot can never observe an inserted-but-unstarted entry.
            activation = lazy.Value;
        }

        ActivatedToolHarnessPipeline entry;
        try { entry = await activation.ConfigureAwait(false); }
        catch
        {
            _entries.TryRemove(new KeyValuePair<string, Lazy<Task<ActivatedToolHarnessPipeline>>>(identity, lazy));
            throw;
        }
        return await entry.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    internal ToolHarnessRegistryDiagnostics GetDiagnosticsSnapshot() => new(
        _entries.Select(static pair =>
                pair.Value.IsValueCreated && pair.Value.Value.IsCompletedSuccessfully
                    ? pair.Value.Value.Result.GetDiagnostics()
                    : new ToolHarnessRegistryEntryDiagnostics(
                        pair.Key, 0, ToolHarnessPipelineLifecycleState.Activating, 0))
            .OrderBy(static value => value.ActivationOrdinal)
            .ToArray());

    internal async ValueTask DeactivateAllAsync(ToolHarnessDeactivationReason reason)
    {
        Lazy<Task<ActivatedToolHarnessPipeline>>[] entries;
        lock (_gate)
        {
            if (_closing != 0) return;
            _closing = 1;
            entries = _entries.Values.ToArray();
        }

        List<Exception>? failures = null;
        var activated = new List<ActivatedToolHarnessPipeline>();
        foreach (var lazy in entries)
        {
            if (!lazy.IsValueCreated) continue;
            try { activated.Add(await lazy.Value.ConfigureAwait(false)); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }

        foreach (var entry in activated.OrderByDescending(static value => value.ActivationOrdinal))
        {
            try { await entry.DeactivateAsync(reason).ConfigureAwait(false); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        _entries.Clear();
        if (failures is { Count: > 0 })
            throw new AggregateException("One or more ToolHarness pipelines failed to deactivate.", failures);
    }

    private async Task<ActivatedToolHarnessPipeline> ActivateAsync(
        ToolHarnessFactory harness,
        ToolHarnessActivationContext context,
        CancellationToken cancellationToken)
    {
        var instances = new List<ToolHarnessMiddlewareActivation>();
        var activatedLifecycleCount = 0;
        try
        {
            foreach (var descriptor in harness.Middleware ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var descriptorContext = context.ForMiddleware(descriptor.MiddlewareType, "factory");
                var activation = descriptor.Factory(descriptorContext) ??
                    throw new InvalidOperationException($"ToolHarness '{harness.ActivationIdentity}' middleware factory returned null.");
                if (activation.Middleware.GetType() != descriptor.MiddlewareType)
                    throw new InvalidOperationException($"ToolHarness '{harness.ActivationIdentity}' middleware factory returned '{activation.Middleware.GetType()}' instead of exact declared type '{descriptor.MiddlewareType}'.");
                if (activation.Ownership == ToolHarnessMiddlewareOwnership.Services &&
                    (descriptorContext.Services is null || !descriptorContext.WasResolvedFromServices(activation.Middleware)))
                    throw new InvalidOperationException(
                        $"ToolHarness '{harness.ActivationIdentity}' services-owned middleware '{descriptor.MiddlewareType}' " +
                        "must be the exact instance resolved through its input child-scope activation context.");
                instances.Add(activation);
            }

            foreach (var activation in instances)
            {
                if (activation.Middleware is IToolHarnessMiddlewareLifecycle lifecycle)
                    await lifecycle.OnHarnessActivatedAsync(
                        context.ForMiddleware(
                            activation.Middleware.GetType(),
                            nameof(IToolHarnessMiddlewareLifecycle.OnHarnessActivatedAsync)),
                        cancellationToken).ConfigureAwait(false);
                activatedLifecycleCount++;
            }

            return new ActivatedToolHarnessPipeline(
                harness.ActivationIdentity,
                context.InputExecutionId,
                new AgentMiddlewarePipeline(instances.Select(static value => (IAgentMiddleware)value.Middleware).ToArray()),
                instances,
                Interlocked.Increment(ref _activationOrdinal));
        }
        catch (Exception activationFailure)
        {
            var cleanupFailures = await ActivatedToolHarnessPipeline.UnwindFailedActivationAsync(
                harness.ActivationIdentity, context.InputExecutionId, instances, activatedLifecycleCount).ConfigureAwait(false);
            if (cleanupFailures.Count > 0)
                throw new AggregateException("ToolHarness activation and cleanup failed.", [activationFailure, .. cleanupFailures]);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(activationFailure).Throw();
            throw;
        }
    }
}

internal sealed class ActivatedToolHarnessPipeline
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<ToolHarnessMiddlewareActivation> _instances;
    private readonly TaskCompletionSource _admissionsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ToolHarnessPipelineLifecycleState _state = ToolHarnessPipelineLifecycleState.Active;
    private int _admissions;

    internal ActivatedToolHarnessPipeline(
        string harnessIdentity,
        string inputExecutionId,
        AgentMiddlewarePipeline pipeline,
        IReadOnlyList<ToolHarnessMiddlewareActivation> instances,
        long activationOrdinal)
    {
        HarnessIdentity = harnessIdentity;
        InputExecutionId = inputExecutionId;
        Pipeline = pipeline;
        _instances = instances;
        ActivationOrdinal = activationOrdinal;
    }

    internal string HarnessIdentity { get; }
    internal string InputExecutionId { get; }
    internal AgentMiddlewarePipeline Pipeline { get; }
    internal long ActivationOrdinal { get; }

    internal ValueTask<ToolHarnessPipelineLease> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_state != ToolHarnessPipelineLifecycleState.Active)
                throw new InvalidOperationException($"ToolHarness '{HarnessIdentity}' is deactivating and rejects new invocation admission.");
            _admissions++;
            return ValueTask.FromResult(new ToolHarnessPipelineLease(this));
        }
    }

    internal ToolHarnessRegistryEntryDiagnostics GetDiagnostics()
    {
        lock (_gate) return new(HarnessIdentity, ActivationOrdinal, _state, _admissions);
    }

    internal ValueTask ReleaseAdmissionAsync()
    {
        lock (_gate)
        {
            if (--_admissions < 0)
                throw new InvalidOperationException("ToolHarness invocation admission was released more than once.");
            if (_admissions == 0 && _state == ToolHarnessPipelineLifecycleState.Deactivating)
                _admissionsDrained.TrySetResult();
        }
        return ValueTask.CompletedTask;
    }

    internal async ValueTask DeactivateAsync(ToolHarnessDeactivationReason reason)
    {
        Task wait;
        lock (_gate)
        {
            if (_state is ToolHarnessPipelineLifecycleState.Deactivating or ToolHarnessPipelineLifecycleState.Disposed)
                return;
            _state = ToolHarnessPipelineLifecycleState.Deactivating;
            if (_admissions == 0) _admissionsDrained.TrySetResult();
            wait = _admissionsDrained.Task;
        }
        await wait.ConfigureAwait(false);

        List<Exception>? failures = null;
        var deactivation = new ToolHarnessDeactivationContext(HarnessIdentity, InputExecutionId, reason);
        for (var index = _instances.Count - 1; index >= 0; index--)
        {
            var activation = _instances[index];
            if (activation.Middleware is IToolHarnessMiddlewareLifecycle lifecycle)
            {
                try { await lifecycle.OnHarnessDeactivatingAsync(deactivation, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            }
        }
        await DisposeOwnedAsync(_instances, failures ??= []).ConfigureAwait(false);
        lock (_gate) _state = ToolHarnessPipelineLifecycleState.Disposed;
        if (failures.Count > 0) throw new AggregateException($"ToolHarness '{HarnessIdentity}' cleanup failed.", failures);
    }

    internal static async ValueTask<IReadOnlyList<Exception>> UnwindFailedActivationAsync(
        string harnessIdentity,
        string inputExecutionId,
        IReadOnlyList<ToolHarnessMiddlewareActivation> instances,
        int activatedLifecycleCount)
    {
        var failures = new List<Exception>();
        var context = new ToolHarnessDeactivationContext(harnessIdentity, inputExecutionId, ToolHarnessDeactivationReason.Failed);
        for (var index = Math.Min(activatedLifecycleCount, instances.Count) - 1; index >= 0; index--)
        {
            if (instances[index].Middleware is not IToolHarnessMiddlewareLifecycle lifecycle) continue;
            try { await lifecycle.OnHarnessDeactivatingAsync(context, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { failures.Add(ex); }
        }
        await DisposeOwnedAsync(instances, failures).ConfigureAwait(false);
        return failures;
    }

    private static async ValueTask DisposeOwnedAsync(
        IReadOnlyList<ToolHarnessMiddlewareActivation> instances,
        List<Exception> failures)
    {
        for (var index = instances.Count - 1; index >= 0; index--)
        {
            var activation = instances[index];
            if (activation.Ownership != ToolHarnessMiddlewareOwnership.Execution) continue;
            try
            {
                if (activation.Middleware is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (activation.Middleware is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex) { failures.Add(ex); }
        }
    }
}

internal sealed class ToolHarnessPipelineLease(ActivatedToolHarnessPipeline entry) : IAsyncDisposable
{
    private ActivatedToolHarnessPipeline? _entry = entry;
    internal AgentMiddlewarePipeline Pipeline => (_entry ?? throw new ObjectDisposedException(nameof(ToolHarnessPipelineLease))).Pipeline;
    public ValueTask DisposeAsync()
    {
        var current = Interlocked.Exchange(ref _entry, null);
        return current is null ? ValueTask.CompletedTask : current.ReleaseAdmissionAsync();
    }
}
