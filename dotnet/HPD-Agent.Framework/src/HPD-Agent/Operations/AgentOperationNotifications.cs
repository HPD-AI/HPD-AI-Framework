using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Contains one bounded semantic operation notification.</summary>
public sealed record AgentOperationNotification
{
    /// <summary>Gets the unique notification identifier.</summary>
    public required string NotificationId { get; init; }
    /// <summary>Gets the authoritative operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the stable operation name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the lowercase provider state.</summary>
    public required string ProviderStatus { get; init; }
    /// <summary>Gets a bounded completion or failure summary.</summary>
    public string? Summary { get; init; }
}

/// <summary>Requests semantic delivery of accepted operation notifications.</summary>
public sealed record AgentOperationNotificationInputEvent(
    IReadOnlyList<AgentOperationNotification> Notifications) : AgentInputEvent;

/// <summary>Records that an operation notification was durably queued.</summary>
public sealed record AgentOperationNotificationQueuedEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    /// <summary>Gets the queued notification.</summary>
    public required AgentOperationNotification Notification { get; init; }
    /// <summary>Gets the queue time.</summary>
    public required DateTimeOffset QueuedAt { get; init; }
}

/// <summary>Records successful semantic delivery of one operation notification.</summary>
public sealed record AgentOperationNotificationDeliveredEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Lifecycle;
    /// <summary>Gets the notification identifier.</summary>
    public required string NotificationId { get; init; }
    /// <summary>Gets the delivery time.</summary>
    public required DateTimeOffset DeliveredAt { get; init; }
}

/// <summary>Records a policy or deduplication suppression decision.</summary>
public sealed record AgentOperationNotificationSuppressedEvent : AgentEvent
{
    /// <inheritdoc />
    public override HPD.Events.EventKind Kind { get; init; } = HPD.Events.EventKind.Diagnostic;
    /// <summary>Gets the authoritative operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the stable suppression reason.</summary>
    public required string Reason { get; init; }
    /// <summary>Gets the decision time.</summary>
    public required DateTimeOffset SuppressedAt { get; init; }
}

/// <summary>Dispatches operation transition notifications through the semantic input lane.</summary>
internal sealed class AgentOperationNotificationDispatcher : IDisposable
{
    private readonly IAgentEventPublisher? _threadEvents;
    private readonly HPD.Events.IEventCoordinator _events;
    private readonly System.Threading.Channels.ChannelWriter<AgentInputEvent> _input;
    private readonly IDisposable _subscription;
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _lastDelivery = new(StringComparer.Ordinal);
    private readonly HashSet<string> _terminalDeliveries = new(StringComparer.Ordinal);
    private AgentRunConfig? _runConfig;
    private int _disposed;

    internal AgentOperationNotificationDispatcher(
        HPD.Events.IEventCoordinator events,
        IAgentEventPublisher? threadEvents,
        System.Threading.Channels.ChannelWriter<AgentInputEvent> input,
        AgentRunConfig? runConfig)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _threadEvents = threadEvents;
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _runConfig = runConfig;
        _subscription = events.Subscribe<AgentOperationTransitionedEvent>(HandleAsync);
    }

    internal void UpdateRunConfig(AgentRunConfig? runConfig)
    {
        if (runConfig is not null)
            lock (_lock) _runConfig = runConfig;
    }

    private async ValueTask HandleAsync(AgentOperationTransitionedEvent evt)
    {
        var operation = evt.Operation;
        var terminal = operation.ProviderStatus is AgentOperationProviderStatus.Completed or
            AgentOperationProviderStatus.Failed or AgentOperationProviderStatus.Cancelled;
        var reason = Evaluate(operation, terminal);
        if (reason is not null)
        {
            await PublishAsync(new AgentOperationNotificationSuppressedEvent
            {
                OperationId = operation.OperationId,
                Reason = reason,
                SuppressedAt = DateTimeOffset.UtcNow,
                SessionId = evt.SessionId,
                ThreadId = evt.ThreadId,
                ThreadExecutionId = evt.ThreadExecutionId
            }).ConfigureAwait(false);
            return;
        }

        var notification = new AgentOperationNotification
        {
            NotificationId = Guid.NewGuid().ToString("N"),
            OperationId = operation.OperationId,
            Name = operation.Name,
            ProviderStatus = operation.ProviderStatus.ToString().ToLowerInvariant(),
            Summary = Bound(operation.Completion?.Summary ?? operation.Failure?.Message)
        };
        await PublishAsync(new AgentOperationNotificationQueuedEvent
        {
            Notification = notification,
            QueuedAt = DateTimeOffset.UtcNow,
            SessionId = evt.SessionId,
            ThreadId = evt.ThreadId,
            ThreadExecutionId = evt.ThreadExecutionId
        }).ConfigureAwait(false);
        AgentRunConfig? runConfig;
        lock (_lock) runConfig = _runConfig;
        await _input.WriteAsync(new AgentOperationNotificationInputEvent([notification])
        {
            AgentId = operation.Address.AgentId,
            SessionId = evt.SessionId,
            ThreadId = evt.ThreadId,
            ThreadExecutionId = evt.ThreadExecutionId,
            RunConfig = runConfig
        }).ConfigureAwait(false);
    }

    private string? Evaluate(AgentOperationSnapshot operation, bool terminal)
    {
        var now = DateTimeOffset.UtcNow;
        var policyKey = string.IsNullOrWhiteSpace(operation.Notification.DeduplicationKey)
            ? operation.OperationId
            : operation.Notification.DeduplicationKey;
        lock (_lock)
        {
            if (terminal && !operation.Notification.IncludeTerminal) return "terminal-disabled";
            if (!terminal && !operation.Notification.IncludeProgress) return "progress-disabled";
            if (terminal && !_terminalDeliveries.Add(policyKey)) return "terminal-duplicate";
            if (!terminal && _lastDelivery.TryGetValue(policyKey, out var last) &&
                now - last < operation.Notification.MinimumInterval) return "minimum-interval";
            _lastDelivery[policyKey] = now;
            return null;
        }
    }

    private async ValueTask PublishAsync(AgentEvent evt)
    {
        if (_threadEvents is not null && evt.SessionId is not null && evt.ThreadId is not null)
            await _threadEvents.CommitAndPublishAsync(new ThreadKey(evt.SessionId, evt.ThreadId), evt).ConfigureAwait(false);
        else
            await _events.EmitAsync(evt).ConfigureAwait(false);
    }

    internal static UserMessagesInputEvent ToUserMessagesInput(AgentOperationNotificationInputEvent input) => new()
    {
        Messages = [new ChatMessage(ChatRole.User, string.Join("\n", input.Notifications.Select(static notification =>
            $"Operation {notification.OperationId} ({notification.Name}) is {notification.ProviderStatus}: {notification.Summary}")))]
    };

    private static string? Bound(string? value) => value is null || value.Length <= 4096 ? value : value[..4096];
    internal ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _subscription.Dispose();
    }
}
