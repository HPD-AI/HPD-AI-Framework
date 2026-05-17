using HPD.Events;
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
public sealed class AgentRuntimeContext : IAsyncDisposable, IAgentBackgroundTaskRegistry
{
    private readonly List<Task> _backgroundTasks = new();
    private readonly List<IDisposable> _disposables = new();
    private readonly List<IAsyncDisposable> _asyncDisposables = new();
    private readonly ChannelWriter<AgentInputEvent> _runtimeInputWriter;
    private readonly Func<InterruptionRequestEvent, CancellationToken, ValueTask> _runtimeInterruptionHandler;
    private readonly Func<bool> _hasActiveRuntimeTurns;
    private readonly object _lock = new();
    private bool _acceptingInputs = true;
    private bool _acceptingBackgroundTasks = true;

    public string AgentName { get; }
    public AgentConfig Config { get; }
    public IServiceProvider? Services { get; }
    public IEventCoordinator EventCoordinator { get; }
    public IStreamRegistry Streams => EventCoordinator.Streams;
    public IRuntimeCapabilityRegistry RuntimeCapabilities { get; } = new RuntimeCapabilityRegistry();
    public string RuntimeId { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? StoppedAt { get; private set; }
    public CancellationToken RuntimeCancellationToken { get; }
    public bool HasActiveRuntimeTurns => _hasActiveRuntimeTurns();

    internal AgentRuntimeContext(
        string agentName,
        AgentConfig config,
        IServiceProvider? services,
        IEventCoordinator eventCoordinator,
        ChannelWriter<AgentInputEvent> runtimeInputWriter,
        Func<InterruptionRequestEvent, CancellationToken, ValueTask> runtimeInterruptionHandler,
        Func<bool> hasActiveRuntimeTurns,
        CancellationToken runtimeCancellationToken)
    {
        AgentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Services = services;
        EventCoordinator = eventCoordinator ?? throw new ArgumentNullException(nameof(eventCoordinator));
        _runtimeInputWriter = runtimeInputWriter ?? throw new ArgumentNullException(nameof(runtimeInputWriter));
        _runtimeInterruptionHandler = runtimeInterruptionHandler ?? throw new ArgumentNullException(nameof(runtimeInterruptionHandler));
        _hasActiveRuntimeTurns = hasActiveRuntimeTurns ?? throw new ArgumentNullException(nameof(hasActiveRuntimeTurns));
        RuntimeCancellationToken = runtimeCancellationToken;
        RuntimeId = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Emit(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        EventCoordinator.Emit(evt);
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

    public void RegisterBackgroundTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (_lock)
        {
            ThrowIfBackgroundRegistrationClosed();
            _backgroundTasks.Add(task);
        }
    }

    public void RegisterBackgroundTask(Func<CancellationToken, Task> taskFactory)
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

    public void RegisterBackgroundTask(
        string name,
        FunctionInvocationSnapshot invocation,
        Func<FunctionBackgroundContext, CancellationToken, Task> taskFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(taskFactory);

        var backgroundContext = new FunctionBackgroundContext
        {
            TaskId = Guid.NewGuid().ToString("N"),
            Name = name,
            Invocation = invocation,
            EventCoordinator = EventCoordinator,
            Services = Services,
            StartedAt = DateTimeOffset.UtcNow
        };

        RegisterBackgroundTask(async runtimeToken =>
        {
            Emit(new ToolCallBackgroundTaskStartedEvent
            {
                TaskId = backgroundContext.TaskId,
                Name = backgroundContext.Name,
                Invocation = backgroundContext.Invocation,
                StartedAt = backgroundContext.StartedAt
            });

            try
            {
                await taskFactory(backgroundContext, runtimeToken).ConfigureAwait(false);

                var completedAt = DateTimeOffset.UtcNow;
                Emit(new ToolCallBackgroundTaskCompletedEvent
                {
                    TaskId = backgroundContext.TaskId,
                    Name = backgroundContext.Name,
                    Invocation = backgroundContext.Invocation,
                    CompletedAt = completedAt,
                    DurationMilliseconds = Math.Max(0, (long)(completedAt - backgroundContext.StartedAt).TotalMilliseconds)
                });
            }
            catch (OperationCanceledException) when (runtimeToken.IsCancellationRequested)
            {
                Emit(new ToolCallBackgroundTaskCancelledEvent
                {
                    TaskId = backgroundContext.TaskId,
                    Name = backgroundContext.Name,
                    Invocation = backgroundContext.Invocation,
                    CancelledAt = DateTimeOffset.UtcNow
                });
                throw;
            }
            catch (Exception ex)
            {
                Emit(new ToolCallBackgroundTaskFaultedEvent
                {
                    TaskId = backgroundContext.TaskId,
                    Name = backgroundContext.Name,
                    Invocation = backgroundContext.Invocation,
                    FaultedAt = DateTimeOffset.UtcNow,
                    ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                    ErrorMessage = ex.Message
                });
                throw;
            }
        });
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
    public IServiceProvider? Services => Base.Services;
    public IEventCoordinator EventCoordinator => Base.EventCoordinator;
    public IStreamRegistry Streams => Base.Streams;
    public IRuntimeCapabilityRegistry RuntimeCapabilities => Base.RuntimeCapabilities;
    public string RuntimeId => Base.RuntimeId;
    public DateTimeOffset CreatedAt => Base.CreatedAt;
    public CancellationToken RuntimeCancellationToken => Base.RuntimeCancellationToken;
    public bool HasActiveRuntimeTurns => Base.HasActiveRuntimeTurns;

    public void Emit(AgentEvent evt) => Base.Emit(evt);

    /// <summary>
    /// Submit semantic user input to the agent runtime loop.
    /// </summary>
    /// <remarks>
    /// The input is enqueued for runtime processing and is not emitted through
    /// <see cref="EventCoordinator"/>.
    /// </remarks>
    public ValueTask RunAsync(AgentInputEvent input, CancellationToken cancellationToken = default) =>
        Base.RunAsync(input, cancellationToken);
    public void RegisterBackgroundTask(Task task) => Base.RegisterBackgroundTask(task);
    public void RegisterBackgroundTask(Func<CancellationToken, Task> taskFactory) => Base.RegisterBackgroundTask(taskFactory);
    public void RegisterDisposable(IDisposable disposable) => Base.RegisterDisposable(disposable);
    public void RegisterAsyncDisposable(IAsyncDisposable disposable) => Base.RegisterAsyncDisposable(disposable);

    protected RuntimeHookContext(AgentRuntimeContext baseContext)
    {
        Base = baseContext ?? throw new ArgumentNullException(nameof(baseContext));
    }
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
