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
    IClientToolOperationRegistry
{
    private readonly List<IDisposable> _disposables = new();
    private readonly List<IAsyncDisposable> _asyncDisposables = new();
    private readonly Dictionary<string, PendingClientToolOperation> _clientToolOperations = new(StringComparer.Ordinal);
    private readonly ChannelWriter<AgentInputEvent> _runtimeInputWriter;
    private readonly Func<AgentInputEvent, CancellationToken, ValueTask> _runtimeInputHandler;
    private readonly Func<bool> _hasActiveRuntimeInputs;
    private readonly object _lock = new();
    private bool _acceptingInputs = true;

    public string AgentName { get; }
    public AgentConfig Config { get; }
    public AgentRunConfig? RunConfig { get; }
    public AgentClientSet? ClientSet { get; }
    public IServiceProvider? Services { get; }
    public IEventCoordinator EventCoordinator { get; }
    public IAgentEventPublisher? ThreadEvents { get; }
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
        Func<AgentInputEvent, CancellationToken, ValueTask> runtimeInputHandler,
        Func<bool> hasActiveRuntimeInputs,
        CancellationToken runtimeCancellationToken,
        IAgentEventPublisher? threadEvents = null,
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
        _runtimeInputHandler = runtimeInputHandler ?? throw new ArgumentNullException(nameof(runtimeInputHandler));
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
            return await ThreadEvents.PublishAsync(
                new ThreadKey(evt.SessionId!, evt.ThreadId!),
                evt,
                cancellationToken).ConfigureAwait(false);
        }

        return await PublishLiveAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AgentEvent> PublishLiveAsync(
        AgentEvent evt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (ThreadEvents is not null)
            return await ThreadEvents.PublishLiveAsync(evt, cancellationToken).ConfigureAwait(false);
        var codec = Config.EventComposition?.Codec
            ?? throw new InvalidOperationException("Runtime context has no event composition authority.");
        if (!codec.TryGetByType(evt.GetType(), out _))
            throw new InvalidOperationException($"Agent event type '{evt.GetType().FullName}' is not present in codec '{codec.Digest}'.");
        var live = evt with { ThreadSequenceNumber = 0 };
        await EventCoordinator.EmitAsync(live, cancellationToken).ConfigureAwait(false);
        return live;
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

        await _runtimeInputHandler(input, cancellationToken).ConfigureAwait(false);
    }

    public ClientToolOperationRegistration RegisterClientToolOperation(
        ClientToolOperationDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ClientOperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.ToolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.RequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.CallId);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.TaskId);

        var pending = new PendingClientToolOperation(descriptor);

        lock (_lock)
        {
            if (!_acceptingInputs || RuntimeCancellationToken.IsCancellationRequested)
                throw new InvalidOperationException(
                    "Agent runtime is stopping or stopped and cannot accept provider operations.");
            if (!_clientToolOperations.TryAdd(descriptor.ClientOperationId, pending))
            {
                throw new InvalidOperationException(
                    $"A client tool background operation with id '{descriptor.ClientOperationId}' is already registered.");
            }
        }

        return new ClientToolOperationRegistration(
            descriptor.ClientOperationId,
            descriptor.TaskId,
            pending.Completion);
    }

    public bool TryResolveClientToolOperation(ClientToolOperationOutcomeEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ClientOperationId);

        if (!TryRemoveClientToolOperation(input.ClientOperationId, out var pending))
            return false;

        return pending.TrySetResult(new ClientToolOperationResult
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

    private bool TryRemoveClientToolOperation(
        string clientOperationId,
        out PendingClientToolOperation pending)
    {
        lock (_lock)
        {
            if (_clientToolOperations.Remove(clientOperationId, out pending!))
                return true;
        }

        pending = null!;
        return false;
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

    internal async Task DisposeRegisteredResourcesAsync(CancellationToken cancellationToken)
    {
        List<Exception>? exceptions = null;
        List<IAsyncDisposable> asyncDisposables;
        List<IDisposable> disposables;

        lock (_lock)
        {
            asyncDisposables = _asyncDisposables.ToList();
            disposables = _disposables.ToList();
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
    public IAgentEventPublisher? ThreadEvents => Base.ThreadEvents;
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
    public ClientToolOperationRegistration RegisterClientToolOperation(ClientToolOperationDescriptor descriptor) => Base.RegisterClientToolOperation(descriptor);
    public bool TryResolveClientToolOperation(ClientToolOperationOutcomeEvent input) => Base.TryResolveClientToolOperation(input);
    public void RegisterDisposable(IDisposable disposable) => Base.RegisterDisposable(disposable);
    public void RegisterAsyncDisposable(IAsyncDisposable disposable) => Base.RegisterAsyncDisposable(disposable);

    protected RuntimeHookContext(AgentRuntimeContext baseContext)
    {
        Base = baseContext ?? throw new ArgumentNullException(nameof(baseContext));
    }
}

internal sealed class PendingClientToolOperation
{
    private readonly TaskCompletionSource<ClientToolOperationResult> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingClientToolOperation(ClientToolOperationDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public ClientToolOperationDescriptor Descriptor { get; }

    public Task<ClientToolOperationResult> Completion => _completion.Task;

    public bool TrySetResult(ClientToolOperationResult result)
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
