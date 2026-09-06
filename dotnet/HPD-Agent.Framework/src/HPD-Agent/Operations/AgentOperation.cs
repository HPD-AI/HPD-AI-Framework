using HPD.Agent.Middleware;
using System.Text.Json;

namespace HPD.Agent;

/// <summary>Identifies the implementation that owns an agent operation.</summary>
public enum AgentOperationSourceKind
{
    /// <summary>A tool invocation scheduled and owned by HPD.</summary>
    LocalTool,
    /// <summary>A durable task owned by a remote MCP server.</summary>
    McpTask,
    /// <summary>A subagent execution.</summary>
    SubAgent,
    /// <summary>A workflow execution.</summary>
    Workflow,
    /// <summary>A multi-agent execution.</summary>
    MultiAgent,
    /// <summary>An operation owned by another provider.</summary>
    ProviderOperation
}

/// <summary>Describes provider-owned progress independently from local observation.</summary>
public enum AgentOperationProviderStatus
{
    /// <summary>The provider accepted the operation.</summary>
    Accepted,
    /// <summary>The provider is executing the operation.</summary>
    Running,
    /// <summary>The provider requires additional authorized input.</summary>
    InputRequired,
    /// <summary>The provider completed successfully.</summary>
    Completed,
    /// <summary>The provider reported terminal failure.</summary>
    Failed,
    /// <summary>Cancellation was requested but is not yet confirmed.</summary>
    CancellationRequested,
    /// <summary>The provider confirmed cancellation.</summary>
    Cancelled
}

/// <summary>Describes HPD's observation relationship to provider work.</summary>
public enum AgentOperationObservationStatus
{
    /// <summary>HPD is actively observing the operation.</summary>
    Attached,
    /// <summary>HPD is stopping observation.</summary>
    Detaching,
    /// <summary>Provider work may continue without a live observer.</summary>
    Detached,
    /// <summary>HPD is reconciling durable provider state.</summary>
    Reconciling,
    /// <summary>Observation ended permanently.</summary>
    Stopped
}

/// <summary>Classifies the operation's control mechanism.</summary>
public enum AgentOperationKind { Task, Process, Session, Workflow, Provider }

/// <summary>Declares controls actually supported by an operation controller.</summary>
[Flags]
public enum AgentOperationCapabilities
{
    /// <summary>No live control is available.</summary>
    None = 0,
    /// <summary>The operation accepts cancellation requests.</summary>
    Cancel = 1,
    /// <summary>The operation accepts additional input.</summary>
    Update = 2,
    /// <summary>Observation may detach without terminating provider work.</summary>
    Detach = 4,
    /// <summary>Detached observation may be reconciled.</summary>
    Reconcile = 8
}

/// <summary>Associates an operation with its authoritative HPD execution scope.</summary>
/// <param name="AgentId">The owning agent identity.</param>
/// <param name="SessionId">The owning session identity.</param>
/// <param name="ThreadId">The owning thread identity.</param>
public sealed record AgentExecutionAddress(string AgentId, string SessionId, string ThreadId);

/// <summary>Projects the operation's stable control surface.</summary>
/// <param name="HandleId">An optional implementation handle.</param>
/// <param name="Kind">The control mechanism kind.</param>
/// <param name="Capabilities">The closed set of supported controls.</param>
public sealed record AgentOperationControl(
    string? HandleId,
    AgentOperationKind Kind,
    AgentOperationCapabilities Capabilities);

/// <summary>Controls which operation transitions may become semantic agent input.</summary>
public sealed record AgentOperationNotificationPolicy
{
    /// <summary>Deliver requests for input separately from routine progress updates.</summary>
    public bool IncludeInputRequired { get; init; } = true;
    /// <summary>Gets whether non-terminal progress may be delivered.</summary>
    public bool IncludeProgress { get; init; }
    /// <summary>Gets whether terminal transitions may be delivered.</summary>
    public bool IncludeTerminal { get; init; } = true;
    /// <summary>Gets an optional stable deduplication key.</summary>
    public string? DeduplicationKey { get; init; }
    /// <summary>Gets the minimum interval between progress deliveries.</summary>
    public TimeSpan MinimumInterval { get; init; }
}

/// <summary>Contains a successful terminal operation result.</summary>
/// <param name="Summary">A bounded, non-secret result summary.</param>
/// <param name="ArtifactReferences">Durable references to result artifacts.</param>
public sealed record AgentOperationCompletion(
    string? Summary,
    IReadOnlyList<string>? ArtifactReferences = null);

/// <summary>Contains a sanitized terminal operation failure.</summary>
/// <param name="Code">The stable machine-readable failure code.</param>
/// <param name="Message">The bounded failure message.</param>
public sealed record AgentOperationFailure(string Code, string Message);

/// <summary>Contains a protected provider reference sufficient to resume observation.</summary>
/// <param name="Kind">The versioned provider-reference discriminator.</param>
/// <param name="ProtectedReference">The protected reference; never a bearer-token projection.</param>
public sealed record AgentOperationRecoveryReference(string Kind, string ProtectedReference);

/// <summary>Contains the complete immutable state of one operation version.</summary>
public sealed record AgentOperationSnapshot
{
    /// <summary>Gets the HPD-authoritative operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the optional provider-authoritative operation identifier.</summary>
    public string? ProviderOperationId { get; init; }
    /// <summary>Gets the operation source.</summary>
    public required AgentOperationSourceKind SourceKind { get; init; }
    /// <summary>Gets the stable operation name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the owning HPD execution address.</summary>
    public required AgentExecutionAddress Address { get; init; }
    /// <summary>Gets the originating thread-execution identifier.</summary>
    public string? OriginatingThreadExecutionId { get; init; }
    /// <summary>Gets the initiating function snapshot when applicable.</summary>
    public FunctionInvocationSnapshot? Invocation { get; init; }
    /// <summary>Gets provider progress.</summary>
    public required AgentOperationProviderStatus ProviderStatus { get; init; }
    /// <summary>Gets local observation progress.</summary>
    public required AgentOperationObservationStatus ObservationStatus { get; init; }
    /// <summary>Gets the available control surface.</summary>
    public required AgentOperationControl Control { get; init; }
    /// <summary>Gets notification policy.</summary>
    public required AgentOperationNotificationPolicy Notification { get; init; }
    /// <summary>Gets registration time.</summary>
    public required DateTimeOffset RegisteredAt { get; init; }
    /// <summary>Gets provider start time when known.</summary>
    public DateTimeOffset? StartedAt { get; init; }
    /// <summary>Gets the last transition time.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
    /// <summary>Gets terminal time when terminal.</summary>
    public DateTimeOffset? FinishedAt { get; init; }
    /// <summary>Gets successful completion details.</summary>
    public AgentOperationCompletion? Completion { get; init; }
    /// <summary>Gets terminal failure details.</summary>
    public AgentOperationFailure? Failure { get; init; }
    /// <summary>Gets the protected recovery reference for non-terminal provider work.</summary>
    public AgentOperationRecoveryReference? Recovery { get; init; }
    /// <summary>Gets the optimistic concurrency version.</summary>
    public required long Version { get; init; }
    /// <summary>Gets bounded, non-secret provider metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Projects the stable model/Hosting registration receipt.</summary>
public sealed record AgentOperationReceipt
{
    /// <summary>Gets the HPD-authoritative operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the optional provider-authoritative identifier.</summary>
    public string? ProviderOperationId { get; init; }
    /// <summary>Gets the source kind.</summary>
    public required AgentOperationSourceKind SourceKind { get; init; }
    /// <summary>Gets the operation name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the owning execution address.</summary>
    public required AgentExecutionAddress Address { get; init; }
    /// <summary>Gets provider status.</summary>
    public required AgentOperationProviderStatus ProviderStatus { get; init; }
    /// <summary>Gets observation status.</summary>
    public required AgentOperationObservationStatus ObservationStatus { get; init; }
    /// <summary>Gets a bounded status message.</summary>
    public string? Message { get; init; }
    /// <summary>Gets the control surface.</summary>
    public required AgentOperationControl Control { get; init; }
    /// <summary>Gets bounded, non-secret metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Requests one version-checked provider or observation transition.</summary>
public sealed record AgentOperationTransition
{
    /// <summary>Gets an optional new provider status.</summary>
    public AgentOperationProviderStatus? ProviderStatus { get; init; }
    /// <summary>Gets an optional new observation status.</summary>
    public AgentOperationObservationStatus? ObservationStatus { get; init; }
    /// <summary>Gets successful terminal details.</summary>
    public AgentOperationCompletion? Completion { get; init; }
    /// <summary>Gets failed terminal details.</summary>
    public AgentOperationFailure? Failure { get; init; }
    /// <summary>Gets a provider state/version key used for idempotency.</summary>
    public string? ProviderDeduplicationKey { get; init; }
    /// <summary>Gets the transition time.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Reports whether a transition changed the aggregate.</summary>
/// <param name="Applied">Whether a new version was committed.</param>
/// <param name="Snapshot">The resulting immutable snapshot.</param>
public sealed record AgentOperationTransitionResult(bool Applied, AgentOperationSnapshot Snapshot);

/// <summary>Contains protocol-neutral JSON responses keyed by provider input-request identifier.</summary>
public sealed record AgentOperationInput
{
    /// <summary>Gets the response values keyed by provider input-request identifier.</summary>
    public required IReadOnlyDictionary<string, JsonElement> Responses { get; init; }
}

/// <summary>Controls live provider work without conflating it with observation cancellation.</summary>
internal interface IAgentOperationController : IAsyncDisposable
{
    ValueTask RequestCancellationAsync(CancellationToken cancellationToken);
    ValueTask SupplyInputAsync(AgentOperationInput input, CancellationToken cancellationToken);
}

/// <summary>Commits canonical operation facts to the owning thread journal.</summary>
internal interface IAgentOperationEventSink
{
    ValueTask AppendAsync(AgentEvent operationEvent, CancellationToken cancellationToken);
}

/// <summary>Owns one operation aggregate and its live controller/observer resources.</summary>
internal sealed class AgentOperation : IAsyncDisposable
{
    private readonly SemaphoreSlim _transitionLock = new(1, 1);
    private readonly HashSet<string> _providerDeduplicationKeys = new(StringComparer.Ordinal);
    private readonly IAgentOperationEventSink _events;
    private AgentOperationSnapshot _snapshot;
    private IAsyncDisposable? _executionOwner;
    private int _disposed;

    internal AgentOperation(
        AgentOperationSnapshot initial,
        IAgentOperationEventSink events,
        IAgentOperationController? controller = null,
        IAsyncDisposable? observer = null,
        IAsyncDisposable? executionOwner = null,
        IEnumerable<string>? providerDeduplicationKeys = null)
    {
        ValidateInitial(initial);
        _snapshot = initial;
        _events = events ?? throw new ArgumentNullException(nameof(events));
        Controller = controller;
        Observer = observer;
        _executionOwner = executionOwner;
        if (providerDeduplicationKeys is not null)
            _providerDeduplicationKeys.UnionWith(providerDeduplicationKeys);
    }

    internal AgentOperationSnapshot Snapshot => Volatile.Read(ref _snapshot);
    internal IAgentOperationController? Controller { get; private set; }
    internal IAsyncDisposable? Observer { get; private set; }
    internal IReadOnlyList<string> ProviderDeduplicationKeys => _providerDeduplicationKeys
        .Order(StringComparer.Ordinal)
        .ToArray();

    internal async ValueTask AttachLiveResourcesAsync(
        IAgentOperationController controller,
        IAsyncDisposable observer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(observer);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 2, this);
            if (_snapshot.ObservationStatus != AgentOperationObservationStatus.Reconciling)
                throw new InvalidOperationException("Live resources may only attach while an operation is reconciling.");
            if (Controller is not null || Observer is not null)
                throw new InvalidOperationException("The operation already owns live resources.");
            Controller = controller;
            Observer = observer;
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    internal async ValueTask<AgentOperationTransitionResult> TransitionAsync(
        AgentOperationTransition transition,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 2, this);
        IAsyncDisposable? executionOwner = null;
        await _transitionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var current = _snapshot;
            if (current.Version != expectedVersion)
                throw new AgentOperationVersionConflictException(expectedVersion, current.Version);
            if (transition.ProviderDeduplicationKey is { Length: > 0 } key &&
                _providerDeduplicationKeys.Contains(key))
                return new(false, current);

            var provider = transition.ProviderStatus ?? current.ProviderStatus;
            var observation = transition.ObservationStatus ?? current.ObservationStatus;
            ValidateProviderTransition(current.ProviderStatus, provider);
            ValidateObservationTransition(current.ObservationStatus, observation);
            ValidateTerminalPayload(provider, transition.Completion, transition.Failure);

            var terminal = provider is AgentOperationProviderStatus.Completed or
                AgentOperationProviderStatus.Failed or AgentOperationProviderStatus.Cancelled;
            var next = current with
            {
                ProviderStatus = provider,
                ObservationStatus = observation,
                StartedAt = current.StartedAt ?? (provider == AgentOperationProviderStatus.Running
                    ? transition.Timestamp : null),
                UpdatedAt = transition.Timestamp,
                FinishedAt = terminal ? transition.Timestamp : current.FinishedAt,
                Completion = transition.Completion ?? current.Completion,
                Failure = transition.Failure ?? current.Failure,
                Recovery = terminal ? null : current.Recovery,
                Version = checked(current.Version + 1)
            };
            await _events.AppendAsync(new AgentOperationTransitionedEvent
            {
                TraceId = current.Invocation?.TraceId,
                SessionId = current.Address.SessionId,
                ThreadId = current.Address.ThreadId,
                ThreadExecutionId = current.OriginatingThreadExecutionId,
                OperationId = current.OperationId,
                PreviousVersion = current.Version,
                Operation = next,
                ProviderDeduplicationKey = transition.ProviderDeduplicationKey
            }, cancellationToken).ConfigureAwait(false);
            if (transition.ProviderDeduplicationKey is { Length: > 0 } committedKey)
                _providerDeduplicationKeys.Add(committedKey);
            Volatile.Write(ref _snapshot, next);
            if (terminal)
                executionOwner = Interlocked.Exchange(ref _executionOwner, null);
            return new(true, next);
        }
        finally
        {
            _transitionLock.Release();
            if (executionOwner is not null)
            {
                try { await executionOwner.DisposeAsync().ConfigureAwait(false); }
                catch { /* Completion on the execution scope retains teardown failure evidence. */ }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
        List<Exception>? failures = null;
        IAsyncDisposable? executionOwner;
        await _transitionLock.WaitAsync().ConfigureAwait(false);
        try
        {
            executionOwner = Interlocked.Exchange(ref _executionOwner, null);
            Volatile.Write(ref _disposed, 2);
        }
        finally
        {
            _transitionLock.Release();
        }
        try
        {
            if (Observer is not null)
                try { await Observer.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            if (Controller is not null)
                try { await Controller.DisposeAsync().ConfigureAwait(false); }
                catch (Exception ex) { (failures ??= []).Add(ex); }
            if (executionOwner is not null)
            {
                try { await executionOwner.DisposeAsync().ConfigureAwait(false); }
                catch { /* Execution-scope Completion retains teardown failure evidence. */ }
            }
            if (failures is { Count: > 0 })
                throw new AggregateException("Agent operation cleanup failed.", failures);
        }
        finally
        {
            Volatile.Write(ref _disposed, 2);
        }
    }

    private static void ValidateInitial(AgentOperationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.Name);
        if (snapshot.Version < 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Operation version cannot be negative.");
        ValidateTerminalPayload(snapshot.ProviderStatus, snapshot.Completion, snapshot.Failure);
        if ((snapshot.ProviderStatus is AgentOperationProviderStatus.Completed or
            AgentOperationProviderStatus.Failed or AgentOperationProviderStatus.Cancelled) &&
            snapshot.FinishedAt is null)
            throw new InvalidOperationException("Terminal operations require a finish time.");
    }

    private static void ValidateProviderTransition(
        AgentOperationProviderStatus current,
        AgentOperationProviderStatus next)
    {
        if (current == next)
            return;
        var valid = current switch
        {
            AgentOperationProviderStatus.Accepted => next is AgentOperationProviderStatus.Running or
                AgentOperationProviderStatus.Failed or AgentOperationProviderStatus.CancellationRequested or
                AgentOperationProviderStatus.Cancelled,
            AgentOperationProviderStatus.Running => next is AgentOperationProviderStatus.InputRequired or
                AgentOperationProviderStatus.Completed or AgentOperationProviderStatus.Failed or
                AgentOperationProviderStatus.CancellationRequested or AgentOperationProviderStatus.Cancelled,
            AgentOperationProviderStatus.InputRequired => next is AgentOperationProviderStatus.Running or
                AgentOperationProviderStatus.Failed or AgentOperationProviderStatus.CancellationRequested or
                AgentOperationProviderStatus.Cancelled,
            AgentOperationProviderStatus.CancellationRequested => next is AgentOperationProviderStatus.Cancelled or
                AgentOperationProviderStatus.Completed or AgentOperationProviderStatus.Failed,
            _ => false
        };
        if (!valid)
            throw new InvalidOperationException($"Invalid provider transition '{current}' -> '{next}'.");
    }

    private static void ValidateObservationTransition(
        AgentOperationObservationStatus current,
        AgentOperationObservationStatus next)
    {
        if (current == next)
            return;
        var valid = current switch
        {
            AgentOperationObservationStatus.Attached => next is AgentOperationObservationStatus.Detaching or
                AgentOperationObservationStatus.Detached or AgentOperationObservationStatus.Stopped,
            AgentOperationObservationStatus.Detaching => next is AgentOperationObservationStatus.Detached or
                AgentOperationObservationStatus.Attached or AgentOperationObservationStatus.Stopped,
            AgentOperationObservationStatus.Detached => next is AgentOperationObservationStatus.Reconciling or
                AgentOperationObservationStatus.Stopped,
            AgentOperationObservationStatus.Reconciling => next is AgentOperationObservationStatus.Attached or
                AgentOperationObservationStatus.Detached or AgentOperationObservationStatus.Stopped,
            _ => false
        };
        if (!valid)
            throw new InvalidOperationException($"Invalid observation transition '{current}' -> '{next}'.");
    }

    private static void ValidateTerminalPayload(
        AgentOperationProviderStatus status,
        AgentOperationCompletion? completion,
        AgentOperationFailure? failure)
    {
        if (status == AgentOperationProviderStatus.Completed && (completion is null || failure is not null))
            throw new InvalidOperationException("Completed operations require completion details and cannot contain failure details.");
        if (status == AgentOperationProviderStatus.Failed && (failure is null || completion is not null))
            throw new InvalidOperationException("Failed operations require failure details and cannot contain completion details.");
        if (status is not AgentOperationProviderStatus.Completed and not AgentOperationProviderStatus.Failed &&
            (completion is not null || failure is not null))
            throw new InvalidOperationException("Non-terminal operations cannot contain completion or failure details.");
    }
}

/// <summary>Reports an optimistic concurrency conflict on an operation transition.</summary>
public sealed class AgentOperationVersionConflictException : InvalidOperationException
{
    /// <summary>Creates a version-conflict exception.</summary>
    /// <param name="expectedVersion">The caller's expected version.</param>
    /// <param name="actualVersion">The aggregate's current version.</param>
    public AgentOperationVersionConflictException(long expectedVersion, long actualVersion)
        : base($"Operation version conflict: expected {expectedVersion}, actual {actualVersion}.")
    {
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }

    /// <summary>Gets the caller's expected version.</summary>
    public long ExpectedVersion { get; }
    /// <summary>Gets the aggregate's current version.</summary>
    public long ActualVersion { get; }
}
