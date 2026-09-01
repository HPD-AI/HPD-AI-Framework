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
    /// <summary>Gets the execution that produced the source operation transition.</summary>
    public string? SourceThreadExecutionId { get; init; }
}

/// <summary>Requests semantic delivery of accepted operation notifications.</summary>
public sealed record AgentOperationNotificationInputEvent(
    IReadOnlyList<AgentOperationNotification> Notifications) : AgentInputEvent;

/// <summary>Records that an operation notification was durably queued.</summary>
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_OPERATION_NOTIFICATION_QUEUED")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_OPERATION_NOTIFICATION_DELIVERED")]
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
[HPD.Agent.Serialization.DurableEvent]
[HPD.Agent.Serialization.EventType("AGENT_OPERATION_NOTIFICATION_SUPPRESSED")]
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
    private readonly Func<AgentOperationNotificationInputEvent, PreparedAgentWorkAdmission> _prepareInput;
    private readonly IDisposable _subscription;
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _lastDelivery = new(StringComparer.Ordinal);
    private readonly HashSet<string> _terminalDeliveries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<bool>> _pendingReservations = new(StringComparer.Ordinal);
    private TaskCompletionSource _admissionsDrained = CompletedDrain();
    private AgentRunConfig? _runConfig;
    private int _inflightAdmissions;
    private bool _stopping;
    private int _disposed;

    internal AgentOperationNotificationDispatcher(
        HPD.Events.IEventCoordinator events,
        IAgentEventPublisher? threadEvents,
        Func<AgentOperationNotificationInputEvent, PreparedAgentWorkAdmission> prepareInput,
        AgentRunConfig? runConfig)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _threadEvents = threadEvents;
        _prepareInput = prepareInput ?? throw new ArgumentNullException(nameof(prepareInput));
        _runConfig = runConfig;
        _subscription = events.Subscribe<AgentOperationTransitionedEvent>(DispatchAsync);
    }

    internal void UpdateRunConfig(AgentRunConfig? runConfig)
    {
        if (runConfig is not null)
            lock (_lock) _runConfig = runConfig;
    }

    private async ValueTask DispatchAsync(AgentOperationTransitionedEvent evt)
    {
        lock (_lock)
        {
            if (_stopping) return;
            if (_inflightAdmissions++ == 0)
                _admissionsDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        try
        {
            await HandleAsync(evt).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                if (--_inflightAdmissions == 0)
                    _admissionsDrained.TrySetResult();
            }
        }
    }

    private async ValueTask HandleAsync(AgentOperationTransitionedEvent evt)
    {
        var operation = evt.Operation;
        var terminal = operation.ProviderStatus is AgentOperationProviderStatus.Completed or
            AgentOperationProviderStatus.Failed or AgentOperationProviderStatus.Cancelled;
        var decision = await EvaluateAsync(operation, terminal).ConfigureAwait(false);
        var reason = decision.SuppressionReason;
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
            Summary = Bound(operation.Completion?.Summary ?? operation.Failure?.Message),
            SourceThreadExecutionId = evt.ThreadExecutionId
        };
        try
        {
            _ = FormatNotifications([notification]);
        }
        catch (Exception exception) when (exception is ArgumentException or System.Xml.XmlException)
        {
            Rollback(decision);
            await PublishAsync(new AgentOperationNotificationSuppressedEvent
            {
                OperationId = operation.OperationId,
                Reason = "invalid-notification-payload",
                SuppressedAt = DateTimeOffset.UtcNow,
                SessionId = evt.SessionId,
                ThreadId = evt.ThreadId,
                ThreadExecutionId = evt.ThreadExecutionId
            }).ConfigureAwait(false);
            return;
        }
        AgentRunConfig? runConfig;
        lock (_lock) runConfig = _runConfig;
        try
        {
            using var prepared = _prepareInput(new AgentOperationNotificationInputEvent([notification])
            {
                AgentId = operation.Address.AgentId,
                SessionId = evt.SessionId,
                ThreadId = evt.ThreadId,
                RunConfig = runConfig
            });
            await PublishAsync(new AgentOperationNotificationQueuedEvent
            {
                Notification = notification,
                QueuedAt = DateTimeOffset.UtcNow,
                SessionId = evt.SessionId,
                ThreadId = evt.ThreadId,
                ThreadExecutionId = prepared.Input.ThreadExecutionId
            }).ConfigureAwait(false);
            Commit(decision);
            prepared.CommitVisible();
        }
        catch
        {
            Rollback(decision);
            throw;
        }
    }

    private async ValueTask<DeliveryDecision> EvaluateAsync(AgentOperationSnapshot operation, bool terminal)
    {
        var policyKey = string.IsNullOrWhiteSpace(operation.Notification.DeduplicationKey)
            ? operation.OperationId
            : operation.Notification.DeduplicationKey;
        while (true)
        {
            Task<bool>? pending = null;
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                if (terminal && !operation.Notification.IncludeTerminal) return DeliveryDecision.Suppressed("terminal-disabled");
                if (!terminal && !operation.Notification.IncludeProgress) return DeliveryDecision.Suppressed("progress-disabled");
                if (terminal && _terminalDeliveries.Contains(policyKey)) return DeliveryDecision.Suppressed("terminal-duplicate");
                if (!terminal && _lastDelivery.TryGetValue(policyKey, out var last) &&
                    now - last < operation.Notification.MinimumInterval) return DeliveryDecision.Suppressed("minimum-interval");
                if (_pendingReservations.TryGetValue(policyKey, out var existing))
                {
                    pending = existing.Task;
                }
                else
                {
                    var reservation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingReservations.Add(policyKey, reservation);
                    return new DeliveryDecision(null, policyKey, now, terminal, reservation);
                }
            }

            await pending.ConfigureAwait(false);
        }
    }

    private void Commit(DeliveryDecision decision)
    {
        if (decision.PolicyKey is null || decision.ReservedAt is null || decision.Reservation is null) return;
        lock (_lock)
        {
            _lastDelivery[decision.PolicyKey] = decision.ReservedAt.Value;
            if (decision.Terminal)
                _terminalDeliveries.Add(decision.PolicyKey);
            _pendingReservations.Remove(decision.PolicyKey);
            decision.Reservation.TrySetResult(true);
        }
    }

    private void Rollback(DeliveryDecision decision)
    {
        if (decision.PolicyKey is null || decision.Reservation is null) return;
        lock (_lock)
        {
            _pendingReservations.Remove(decision.PolicyKey);
            decision.Reservation.TrySetResult(false);
        }
    }

    private async ValueTask PublishAsync(AgentEvent evt)
    {
        if (_threadEvents is not null && evt.SessionId is not null && evt.ThreadId is not null)
            await _threadEvents.CommitAndPublishAsync(new ThreadKey(evt.SessionId, evt.ThreadId), evt).ConfigureAwait(false);
        else
            await _events.EmitAsync(evt).ConfigureAwait(false);
    }

    internal static UserMessagesInputEvent ToNotificationTurnInput(AgentOperationNotificationInputEvent input) => new()
    {
        Messages =
        [
            new ChatMessage(ChatRole.System, FormatNotifications(input.Notifications))
                .WithPolicy(
                    AgentMessageSource.BackgroundNotification,
                    AgentMessageVisibility.Hidden,
                    AgentMessagePersistence.ModelContextOnly)
        ],
        ClientInputId = input.ClientInputId,
        AgentId = input.AgentId,
        SessionId = input.SessionId,
        ThreadId = input.ThreadId,
        ThreadExecutionId = input.ThreadExecutionId,
        RunConfig = input.RunConfig
    };

    internal static string FormatNotifications(IReadOnlyList<AgentOperationNotification> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        if (notifications.Count is 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(notifications), "Notification batches must contain between 1 and 32 items.");

        var builder = new System.Text.StringBuilder();
        using (var writer = System.Xml.XmlWriter.Create(builder, new System.Xml.XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            ConformanceLevel = System.Xml.ConformanceLevel.Document
        }))
        {
            writer.WriteStartElement("agent-operation-notifications");
            foreach (var notification in notifications)
            {
                ValidateField(notification.NotificationId, 256, nameof(notification.NotificationId));
                ValidateField(notification.OperationId, 256, nameof(notification.OperationId));
                ValidateField(notification.Name, 512, nameof(notification.Name));
                ValidateField(notification.ProviderStatus, 64, nameof(notification.ProviderStatus));
                if (!Enum.TryParse<AgentOperationProviderStatus>(notification.ProviderStatus, true, out var parsedStatus) ||
                    !string.Equals(notification.ProviderStatus, parsedStatus.ToString().ToLowerInvariant(), StringComparison.Ordinal))
                {
                    throw new ArgumentException("Provider status must use the lowercase canonical provider-status vocabulary.", nameof(notifications));
                }

                writer.WriteStartElement("notification");
                writer.WriteAttributeString("id", notification.NotificationId);
                writer.WriteAttributeString("operation-id", notification.OperationId);
                writer.WriteAttributeString("name", notification.Name);
                writer.WriteAttributeString("status", notification.ProviderStatus);
                if (notification.Summary is not null)
                {
                    writer.WriteStartElement("summary");
                    writer.WriteString(Bound(notification.Summary)!);
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
        var formatted = builder.ToString();
        if (System.Text.Encoding.UTF8.GetByteCount(formatted) > 64 * 1024)
            throw new ArgumentOutOfRangeException(nameof(notifications), "Formatted notification batch exceeds 64 KiB.");
        return formatted;
    }

    private static void ValidateField(string value, int maximumUtf8Bytes, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || System.Text.Encoding.UTF8.GetByteCount(value) > maximumUtf8Bytes)
            throw new ArgumentException($"{name} must be non-empty and at most {maximumUtf8Bytes} UTF-8 bytes.", name);
    }

    private static string? Bound(string? value)
    {
        if (value is null || System.Text.Encoding.UTF8.GetByteCount(value) <= 4096) return value;

        var builder = new System.Text.StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 4096) break;
            builder.Append(rune);
            bytes += rune.Utf8SequenceLength;
        }
        return builder.ToString();
    }

    private readonly record struct DeliveryDecision(
        string? SuppressionReason,
        string? PolicyKey,
        DateTimeOffset? ReservedAt,
        bool Terminal,
        TaskCompletionSource<bool>? Reservation)
    {
        internal static DeliveryDecision Suppressed(string reason) => new(reason, null, null, false, null);
    }
    internal async ValueTask CompleteAsync(CancellationToken cancellationToken)
    {
        Task drained;
        lock (_lock)
        {
            _stopping = true;
            drained = _admissionsDrained.Task;
        }
        Dispose();
        await drained.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _subscription.Dispose();
    }

    private static TaskCompletionSource CompletedDrain()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
