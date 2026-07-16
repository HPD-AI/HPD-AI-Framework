using HPD.Events;
using HPD.Events.Struct;
using HPD.Agent.ClientTools;
using System.Threading.Channels;

namespace HPD.Agent.Middleware;

/// <summary>
/// Reason the agent runtime is stopping.
/// </summary>
public enum RuntimeStopReason
{
    UserRequested,
    Disposed,
    CancellationRequested,
    Faulted
}

/// <summary>
/// Runtime-scoped context used by start/stop middleware hooks.
/// </summary>
public sealed class AgentRuntimeContext :
    IAsyncDisposable,
    IAgentBackgroundTaskRegistry,
    IAgentBackgroundHandleRegistry,
    IClientToolBackgroundOperationRegistry
{
    private readonly List<Task> _backgroundTasks = new();
    private readonly List<IDisposable> _disposables = new();
    private readonly List<IAsyncDisposable> _asyncDisposables = new();
    private readonly Dictionary<string, RegisteredBackgroundHandle> _backgroundHandles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PendingClientToolBackgroundOperation> _clientToolBackgroundOperations = new(StringComparer.Ordinal);
    private readonly ChannelWriter<AgentInputEvent> _runtimeInputWriter;
    private readonly Func<InterruptionRequestEvent, CancellationToken, ValueTask> _runtimeInterruptionHandler;
    private readonly Func<bool> _hasActiveRuntimeInputs;
    private readonly object _lock = new();
    private bool _acceptingInputs = true;
    private bool _acceptingBackgroundTasks = true;

    public string AgentName { get; }
    public AgentConfig Config { get; }
    public AgentRunConfig? RunConfig { get; }
    public AgentClientSet? ClientSet { get; }
    public IServiceProvider? Services { get; }
    public IEventCoordinator EventCoordinator { get; }
    public IThreadEventPublisher? ThreadEvents { get; }
    public IEventFlowRegistry EventFlows => EventCoordinator.EventFlows;
    public IStructEventHub StructEvents { get; }
    public IContentStore? ContentStore { get; }
    public IRuntimeCapabilityRegistry RuntimeCapabilities { get; } = new RuntimeCapabilityRegistry();
    public string RuntimeId { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? StoppedAt { get; private set; }
    public CancellationToken RuntimeCancellationToken { get; }
    public bool HasActiveRuntimeInputs => _hasActiveRuntimeInputs();

    internal AgentRuntimeContext(
        string agentName,
        AgentConfig config,
        IServiceProvider? services,
        IEventCoordinator eventCoordinator,
        IStructEventHub structEvents,
        ChannelWriter<AgentInputEvent> runtimeInputWriter,
        Func<InterruptionRequestEvent, CancellationToken, ValueTask> runtimeInterruptionHandler,
        Func<bool> hasActiveRuntimeInputs,
        CancellationToken runtimeCancellationToken,
        IThreadEventPublisher? threadEvents = null,
        AgentClientSet? clientSet = null,
        AgentRunConfig? runConfig = null,
        IContentStore? contentStore = null)
    {
        AgentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Services = services;
        EventCoordinator = eventCoordinator ?? throw new ArgumentNullException(nameof(eventCoordinator));
        ThreadEvents = threadEvents;
        StructEvents = structEvents ?? throw new ArgumentNullException(nameof(structEvents));
        _runtimeInputWriter = runtimeInputWriter ?? throw new ArgumentNullException(nameof(runtimeInputWriter));
        _runtimeInterruptionHandler = runtimeInterruptionHandler ?? throw new ArgumentNullException(nameof(runtimeInterruptionHandler));
        _hasActiveRuntimeInputs = hasActiveRuntimeInputs ?? throw new ArgumentNullException(nameof(hasActiveRuntimeInputs));
        ClientSet = clientSet;
        RunConfig = runConfig;
        ContentStore = contentStore;
        RuntimeCancellationToken = runtimeCancellationToken;
        RuntimeId = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public async ValueTask<AgentEvent> PublishAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var hasSession = !string.IsNullOrWhiteSpace(evt.SessionId);
        var hasThread = !string.IsNullOrWhiteSpace(evt.ThreadId);
        if (hasSession != hasThread)
            throw new InvalidOperationException("A canonical event must provide both SessionId and ThreadId.");

        if (hasSession && ThreadEvents is not null)
        {
            return await ThreadEvents.CommitAndPublishAsync(
                new ThreadKey(evt.SessionId!, evt.ThreadId!),
                evt,
                cancellationToken).ConfigureAwait(false);
        }

        await EventCoordinator.EmitAsync(evt, cancellationToken).ConfigureAwait(false);
        return evt;
    }

    /// <summary>
    /// Submit user input or runtime control input to the agent runtime loop.
    /// </summary>
    /// <remarks>
    /// The input is enqueued for the runtime loop and is not executed inline. Use
    /// <see cref="Emit(AgentEvent)"/> for observation events instead.
    /// </remarks>
    public async ValueTask RunAsync(
        AgentInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        lock (_lock)
        {
            if (!_acceptingInputs)
                throw new InvalidOperationException("Agent runtime is stopping or stopped and cannot accept user input.");
        }

        if (RuntimeCancellationToken.IsCancellationRequested)
            throw new InvalidOperationException("Agent runtime is stopping or stopped and cannot accept user input.");

        if (input is InterruptionRequestEvent interruption)
        {
            await _runtimeInterruptionHandler(interruption, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _runtimeInputWriter.WriteAsync(input, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            throw new InvalidOperationException(
                "Agent runtime is stopping or stopped and cannot accept user input.",
                ex);
        }
    }

    private void RegisterBackgroundTaskCore(Func<CancellationToken, Task> taskFactory)
    {
        ArgumentNullException.ThrowIfNull(taskFactory);

        Task task;
        lock (_lock)
        {
            ThrowIfBackgroundRegistrationClosed();
            task = taskFactory(RuntimeCancellationToken);
            _backgroundTasks.Add(task);
        }
    }

    public BackgroundTaskRegistration RegisterBackgroundTask(
        BackgroundTaskDescriptor descriptor,
        Func<BackgroundTaskContext, CancellationToken, Task> taskFactory)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Name);
        ArgumentNullException.ThrowIfNull(taskFactory);

        var backgroundContext = new BackgroundTaskContext
        {
            TaskId = Guid.NewGuid().ToString("N"),
            Descriptor = descriptor,
            EventCoordinator = EventCoordinator,
            ThreadEvents = ThreadEvents,
            Services = Services,
            StartedAt = DateTimeOffset.UtcNow
        };

        RegisterBackgroundTaskCore(async runtimeToken =>
        {
            await PublishAsync(new BackgroundTaskStartedEvent
            {
                TaskId = backgroundContext.TaskId,
                Name = backgroundContext.Name,
                SourceKind = backgroundContext.SourceKind,
                SourceId = backgroundContext.SourceId,
                ParentRuntimeRunId = backgroundContext.ParentRuntimeRunId,
                SessionId = backgroundContext.SessionId ?? backgroundContext.Invocation?.SessionId,
                ThreadId = backgroundContext.ThreadId ?? backgroundContext.Invocation?.ThreadId,
                Notification = backgroundContext.Notification,
                Invocation = backgroundContext.Invocation,
                Metadata = backgroundContext.Metadata,
                StartedAt = backgroundContext.StartedAt
            }, runtimeToken).ConfigureAwait(false);

            try
            {
                await taskFactory(backgroundContext, runtimeToken).ConfigureAwait(false);

                var completedAt = DateTimeOffset.UtcNow;
                var completion = backgroundContext.Completion;
                await PublishAsync(new BackgroundTaskCompletedEvent
                {
                    TaskId = backgroundContext.TaskId,
                    Name = backgroundContext.Name,
                    SourceKind = backgroundContext.SourceKind,
                    SourceId = backgroundContext.SourceId,
                    ParentRuntimeRunId = backgroundContext.ParentRuntimeRunId,
                    SessionId = backgroundContext.SessionId ?? backgroundContext.Invocation?.SessionId,
                    ThreadId = backgroundContext.ThreadId ?? backgroundContext.Invocation?.ThreadId,
                    Notification = backgroundContext.Notification,
                    Invocation = backgroundContext.Invocation,
                    Metadata = MergeMetadata(backgroundContext.Metadata, completion?.Metadata),
                    CompletedAt = completedAt,
                    DurationMilliseconds = Math.Max(0, (long)(completedAt - backgroundContext.StartedAt).TotalMilliseconds),
                    Summary = completion?.Summary
                }, runtimeToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                await PublishAsync(new BackgroundTaskCancelledEvent
                {
                    TaskId = backgroundContext.TaskId,
                    Name = backgroundContext.Name,
                    SourceKind = backgroundContext.SourceKind,
                    SourceId = backgroundContext.SourceId,
                    ParentRuntimeRunId = backgroundContext.ParentRuntimeRunId,
                    SessionId = backgroundContext.SessionId ?? backgroundContext.Invocation?.SessionId,
                    ThreadId = backgroundContext.ThreadId ?? backgroundContext.Invocation?.ThreadId,
                    Notification = backgroundContext.Notification,
                    Invocation = backgroundContext.Invocation,
                    Metadata = MergeMetadata(
                        backgroundContext.Metadata,
                        backgroundContext.Completion?.Metadata),
                    CancelledAt = DateTimeOffset.UtcNow,
                    Reason = runtimeToken.IsCancellationRequested
                        ? "runtime-stopping"
                        : ex.Message
                }, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await PublishAsync(new BackgroundTaskFaultedEvent
                {
                    TaskId = backgroundContext.TaskId,
                    Name = backgroundContext.Name,
                    SourceKind = backgroundContext.SourceKind,
                    SourceId = backgroundContext.SourceId,
                    ParentRuntimeRunId = backgroundContext.ParentRuntimeRunId,
                    SessionId = backgroundContext.SessionId ?? backgroundContext.Invocation?.SessionId,
                    ThreadId = backgroundContext.ThreadId ?? backgroundContext.Invocation?.ThreadId,
                    Notification = backgroundContext.Notification,
                    Invocation = backgroundContext.Invocation,
                    Metadata = backgroundContext.Metadata,
                    FaultedAt = DateTimeOffset.UtcNow,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                    ErrorMessage = ex.Message
                }, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        });

        return new BackgroundTaskRegistration(
            backgroundContext.TaskId,
            backgroundContext.Name,
            backgroundContext.SourceKind);
    }

    public async ValueTask<BackgroundHandleRegistration> RegisterHandleAsync(
        BackgroundHandleDescriptor descriptor,
        IBackgroundHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Name);
        ArgumentNullException.ThrowIfNull(handle);

        var handleId = string.IsNullOrWhiteSpace(descriptor.HandleId)
            ? Guid.NewGuid().ToString("N")
            : descriptor.HandleId;
        var registeredAt = DateTimeOffset.UtcNow;
        var normalizedDescriptor = descriptor with
        {
            HandleId = handleId,
            SourceId = descriptor.SourceId ?? handleId,
            SupportedOperations = descriptor.SupportedOperations == BackgroundHandleOperation.None
                ? BackgroundHandleOperation.Status
                : descriptor.SupportedOperations
        };
        var registered = new RegisteredBackgroundHandle(
            handleId,
            normalizedDescriptor,
            handle,
            registeredAt);

        lock (_lock)
        {
            ThrowIfBackgroundRegistrationClosed();
            if (!_backgroundHandles.TryAdd(handleId, registered))
                throw new InvalidOperationException($"A background handle with id '{handleId}' is already registered.");
        }

        try
        {
            await PublishAsync(new BackgroundHandleRegisteredEvent
            {
                HandleId = handleId,
                Name = normalizedDescriptor.Name,
                HandleKind = normalizedDescriptor.Kind,
                SourceKind = normalizedDescriptor.SourceKind,
                SourceId = normalizedDescriptor.SourceId,
                SessionId = normalizedDescriptor.SessionId ?? normalizedDescriptor.Invocation?.SessionId,
                ThreadId = normalizedDescriptor.ThreadId ?? normalizedDescriptor.Invocation?.ThreadId,
                Invocation = normalizedDescriptor.Invocation,
                SupportedOperations = normalizedDescriptor.SupportedOperations,
                Metadata = normalizedDescriptor.Metadata,
                RegisteredAt = registeredAt
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_lock)
                _backgroundHandles.Remove(handleId);
            throw;
        }

        if (handle is IAsyncDisposable asyncDisposable)
            RegisterAsyncDisposable(asyncDisposable);
        else if (handle is IDisposable disposable)
            RegisterDisposable(disposable);

        return new BackgroundHandleRegistration(
            handleId,
            normalizedDescriptor.Name,
            normalizedDescriptor.Kind,
            normalizedDescriptor.SourceKind);
    }

    public bool TryGetHandle(
        string handleId,
        BackgroundHandleScope scope,
        out RegisteredBackgroundHandle handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handleId);
        ArgumentNullException.ThrowIfNull(scope);

        lock (_lock)
        {
            if (_backgroundHandles.TryGetValue(handleId, out var registered) &&
                IsHandleInScope(registered.Descriptor, scope))
            {
                handle = registered;
                return true;
            }
        }

        handle = null!;
        return false;
    }

    public IReadOnlyList<RegisteredBackgroundHandle> ListHandles(BackgroundHandleQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_lock)
        {
            return _backgroundHandles.Values
                .Where(handle => MatchesQuery(handle.Descriptor, query))
                .ToList();
        }
    }

    public ClientToolBackgroundOperationRegistration RegisterClientToolBackgroundOperation(
        ClientToolBackgroundOperationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ClientOperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ToolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.CallId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.TaskId);

        var pending = new PendingClientToolBackgroundOperation(descriptor);

        lock (_lock)
        {
            ThrowIfBackgroundRegistrationClosed();
            if (!_clientToolBackgroundOperations.TryAdd(descriptor.ClientOperationId, pending))
            {
                throw new InvalidOperationException(
                    $"A client tool background operation with id '{descriptor.ClientOperationId}' is already registered.");
            }
        }

        return new ClientToolBackgroundOperationRegistration(
            descriptor.ClientOperationId,
            descriptor.TaskId,
            pending.Completion);
    }

    public bool TryResolveClientToolBackgroundOperation(ClientToolBackgroundOperationOutcomeEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ClientOperationId);

        if (!TryRemoveClientToolBackgroundOperation(input.ClientOperationId, out var pending))
            return false;

        return pending.TrySetResult(new ClientToolBackgroundOperationResult
        {
            State = input.State,
            Content = input.Content,
            Augmentation = input.Augmentation,
            ErrorMessage = input.ErrorMessage,
            ErrorType = input.ErrorType,
            CancellationReason = input.CancellationReason,
            Metadata = input.Metadata
        });
    }

    private bool TryRemoveClientToolBackgroundOperation(
        string clientOperationId,
        out PendingClientToolBackgroundOperation pending)
    {
        lock (_lock)
        {
            if (_clientToolBackgroundOperations.Remove(clientOperationId, out pending!))
                return true;
        }

        pending = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, string>? MergeMetadata(
        IReadOnlyDictionary<string, string>? descriptorMetadata,
        IReadOnlyDictionary<string, string>? completionMetadata)
    {
        if ((descriptorMetadata is null || descriptorMetadata.Count == 0) &&
            (completionMetadata is null || completionMetadata.Count == 0))
            return null;

        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (descriptorMetadata is not null)
        {
            foreach (var (key, value) in descriptorMetadata)
                merged[key] = value;
        }

        if (completionMetadata is not null)
        {
            foreach (var (key, value) in completionMetadata)
                merged[key] = value;
        }

        return merged;
    }

    private static bool IsHandleInScope(
        BackgroundHandleDescriptor descriptor,
        BackgroundHandleScope scope)
    {
        var sessionId = descriptor.SessionId ?? descriptor.Invocation?.SessionId;
        if (scope.SessionId is not null &&
            !string.Equals(sessionId, scope.SessionId, StringComparison.Ordinal))
        {
            return false;
        }

        var threadId = descriptor.ThreadId ?? descriptor.Invocation?.ThreadId;
        if (scope.ThreadId is not null &&
            !string.Equals(threadId, scope.ThreadId, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesQuery(
        BackgroundHandleDescriptor descriptor,
        BackgroundHandleQuery query)
    {
        if (!IsHandleInScope(
            descriptor,
            new BackgroundHandleScope
            {
                SessionId = query.SessionId,
                ThreadId = query.ThreadId
            }))
        {
            return false;
        }

        if (query.Kind is not null && descriptor.Kind != query.Kind)
            return false;

        if (query.SourceKind is not null && descriptor.SourceKind != query.SourceKind)
            return false;

        return true;
    }

    public void RegisterDisposable(IDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        lock (_lock)
        {
            _disposables.Add(disposable);
        }
    }

    public void RegisterAsyncDisposable(IAsyncDisposable disposable)
    {
        ArgumentNullException.ThrowIfNull(disposable);
        lock (_lock)
        {
            _asyncDisposables.Add(disposable);
        }
    }

    internal void MarkStarted() => StartedAt = DateTimeOffset.UtcNow;

    internal void SealRuntimeCapabilities() => RuntimeCapabilities.Seal();

    internal void MarkStopped() => StoppedAt = DateTimeOffset.UtcNow;

    internal void CompleteInputWriter(Exception? error = null)
    {
        lock (_lock)
        {
            if (!_acceptingInputs)
                return;

            _acceptingInputs = false;
            _runtimeInputWriter.TryComplete(error);
        }
    }

    internal void StopAcceptingBackgroundTaskRegistrations()
    {
        lock (_lock)
        {
            _acceptingBackgroundTasks = false;
        }
    }

    internal async Task DisposeRegisteredResourcesAsync(CancellationToken cancellationToken)
    {
        List<Exception>? exceptions = null;
        List<Task> backgroundTasks;
        List<IAsyncDisposable> asyncDisposables;
        List<IDisposable> disposables;

        lock (_lock)
        {
            backgroundTasks = _backgroundTasks.ToList();
            asyncDisposables = _asyncDisposables.ToList();
            disposables = _disposables.ToList();
        }

        if (backgroundTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(backgroundTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Runtime-owned background work is expected to observe runtime cancellation during stop.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        for (var i = asyncDisposables.Count - 1; i >= 0; i--)
        {
            try
            {
                await asyncDisposables[i].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        for (var i = disposables.Count - 1; i >= 0; i--)
        {
            try
            {
                disposables[i].Dispose();
            }
            catch (Exception ex)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
            throw new AggregateException("One or more runtime resources failed to dispose.", exceptions);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeRegisteredResourcesAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void ThrowIfBackgroundRegistrationClosed()
    {
        if (!_acceptingBackgroundTasks || RuntimeCancellationToken.IsCancellationRequested)
            throw new InvalidOperationException(
                "Agent runtime is stopping or stopped and cannot accept background task registrations.");
    }

    internal BeforeStartContext AsBeforeStart() => new(this);

    internal AfterStartedContext AsAfterStarted()
    {
        var startedAt = StartedAt ?? DateTimeOffset.UtcNow;
        return new(this, startedAt);
    }

    internal BeforeStopContext AsBeforeStop(RuntimeStopReason reason) => new(this, reason);

    internal AfterStoppedContext AsAfterStopped(RuntimeStopReason reason, Exception? error)
    {
        var startedAt = StartedAt ?? CreatedAt;
        var stoppedAt = StoppedAt ?? DateTimeOffset.UtcNow;
        return new(this, reason, startedAt, stoppedAt, stoppedAt - startedAt, error);
    }
}

/// <summary>
/// Base class for runtime lifecycle hook contexts.
/// </summary>
public abstract class RuntimeHookContext
{
    internal AgentRuntimeContext Base { get; }

    public string AgentName => Base.AgentName;
    public AgentConfig Config => Base.Config;
    public AgentRunConfig? RunConfig => Base.RunConfig;
    public AgentClientSet? ClientSet => Base.ClientSet;
    public IServiceProvider? Services => Base.Services;
    public IEventCoordinator EventCoordinator => Base.EventCoordinator;
    public IThreadEventPublisher? ThreadEvents => Base.ThreadEvents;
    public IEventFlowRegistry EventFlows => Base.EventFlows;
    public IStructEventHub StructEvents => Base.StructEvents;
    public IContentStore? ContentStore => Base.ContentStore;
    public IRuntimeCapabilityRegistry RuntimeCapabilities => Base.RuntimeCapabilities;
    public string RuntimeId => Base.RuntimeId;
    public DateTimeOffset CreatedAt => Base.CreatedAt;
    public CancellationToken RuntimeCancellationToken => Base.RuntimeCancellationToken;
    public bool HasActiveRuntimeInputs => Base.HasActiveRuntimeInputs;

    public ValueTask<AgentEvent> PublishAsync(AgentEvent evt, CancellationToken cancellationToken = default)
        => Base.PublishAsync(evt, cancellationToken);

    /// <summary>
    /// Submit semantic user input to the agent runtime loop.
    /// </summary>
    /// <remarks>
    /// The input is enqueued for runtime processing and is not emitted through
    /// <see cref="EventCoordinator"/>.
    /// </remarks>
    public ValueTask RunAsync(AgentInputEvent input, CancellationToken cancellationToken = default) =>
        Base.RunAsync(input, cancellationToken);
    public BackgroundTaskRegistration RegisterBackgroundTask(BackgroundTaskDescriptor descriptor, Func<BackgroundTaskContext, CancellationToken, Task> taskFactory) => Base.RegisterBackgroundTask(descriptor, taskFactory);
    public ValueTask<BackgroundHandleRegistration> RegisterHandleAsync(BackgroundHandleDescriptor descriptor, IBackgroundHandle handle, CancellationToken cancellationToken = default)
        => Base.RegisterHandleAsync(descriptor, handle, cancellationToken);
    public bool TryGetHandle(string handleId, BackgroundHandleScope scope, out RegisteredBackgroundHandle handle) => Base.TryGetHandle(handleId, scope, out handle);
    public IReadOnlyList<RegisteredBackgroundHandle> ListHandles(BackgroundHandleQuery query) => Base.ListHandles(query);
    public ClientToolBackgroundOperationRegistration RegisterClientToolBackgroundOperation(ClientToolBackgroundOperationDescriptor descriptor) => Base.RegisterClientToolBackgroundOperation(descriptor);
    public bool TryResolveClientToolBackgroundOperation(ClientToolBackgroundOperationOutcomeEvent input) => Base.TryResolveClientToolBackgroundOperation(input);
    public void RegisterDisposable(IDisposable disposable) => Base.RegisterDisposable(disposable);
    public void RegisterAsyncDisposable(IAsyncDisposable disposable) => Base.RegisterAsyncDisposable(disposable);

    protected RuntimeHookContext(AgentRuntimeContext baseContext)
    {
        Base = baseContext ?? throw new ArgumentNullException(nameof(baseContext));
    }
}

internal sealed class PendingClientToolBackgroundOperation
{
    private readonly TaskCompletionSource<ClientToolBackgroundOperationResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingClientToolBackgroundOperation(ClientToolBackgroundOperationDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public ClientToolBackgroundOperationDescriptor Descriptor { get; }

    public Task<ClientToolBackgroundOperationResult> Completion => _completion.Task;

    public bool TrySetResult(ClientToolBackgroundOperationResult result)
        => _completion.TrySetResult(result);
}

public sealed class BeforeStartContext : RuntimeHookContext
{
    public bool CancelStart { get; set; }
    public string? CancelReason { get; set; }

    internal BeforeStartContext(AgentRuntimeContext baseContext)
        : base(baseContext)
    {
    }
}

public sealed class AfterStartedContext : RuntimeHookContext
{
    public DateTimeOffset StartedAt { get; }

    internal AfterStartedContext(AgentRuntimeContext baseContext, DateTimeOffset startedAt)
        : base(baseContext)
    {
        StartedAt = startedAt;
    }
}

public sealed class BeforeStopContext : RuntimeHookContext
{
    public RuntimeStopReason Reason { get; }
    public bool DrainPendingInputs { get; set; } = true;
    public TimeSpan? DrainTimeout { get; set; }

    internal BeforeStopContext(AgentRuntimeContext baseContext, RuntimeStopReason reason)
        : base(baseContext)
    {
        Reason = reason;
    }
}

public sealed class AfterStoppedContext : RuntimeHookContext
{
    public RuntimeStopReason Reason { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset StoppedAt { get; }
    public TimeSpan Duration { get; }
    public Exception? Error { get; }

    internal AfterStoppedContext(
        AgentRuntimeContext baseContext,
        RuntimeStopReason reason,
        DateTimeOffset startedAt,
        DateTimeOffset stoppedAt,
        TimeSpan duration,
        Exception? error)
        : base(baseContext)
    {
        Reason = reason;
        StartedAt = startedAt;
        StoppedAt = stoppedAt;
        Duration = duration;
        Error = error;
    }
}
