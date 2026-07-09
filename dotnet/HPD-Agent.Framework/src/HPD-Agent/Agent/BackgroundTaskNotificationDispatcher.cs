using System.Threading.Channels;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal sealed class BackgroundTaskNotificationDispatcher : IDisposable
{
    private static readonly TimeSpan DefaultBatchWindow = TimeSpan.FromMilliseconds(25);

    private readonly string _agentId;
    private readonly HPD.Events.IEventCoordinator _runtimeCoordinator;
    private readonly ChannelWriter<AgentInputEvent> _runtimeWriter;
    private readonly IBackgroundTaskNotificationStrategyRegistry? _strategyRegistry;
    private readonly Channel<BackgroundTaskEvent> _finalStateEvents;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly object _stateLock = new();
    private readonly HashSet<string> _notifiedFinalStateTaskIds = new(StringComparer.Ordinal);
    private readonly Task _pumpTask;
    private readonly TimeSpan _batchWindow;
    private readonly object _disposeLock = new();
    private AgentRunConfig? _runConfig;
    private bool _disposed;

    public BackgroundTaskNotificationDispatcher(
        string agentId,
        HPD.Events.IEventCoordinator runtimeCoordinator,
        ChannelWriter<AgentInputEvent> runtimeWriter,
        AgentRunConfig? runConfig,
        IBackgroundTaskNotificationStrategyRegistry? strategyRegistry = null,
        TimeSpan? batchWindow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(runtimeCoordinator);
        ArgumentNullException.ThrowIfNull(runtimeWriter);

        _agentId = agentId;
        _runtimeCoordinator = runtimeCoordinator;
        _runtimeWriter = runtimeWriter;
        _runConfig = runConfig;
        _strategyRegistry = strategyRegistry;
        _batchWindow = batchWindow ?? DefaultBatchWindow;
        _finalStateEvents = Channel.CreateUnbounded<BackgroundTaskEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _subscriptions.Add(_runtimeCoordinator.Subscribe<BackgroundTaskCompletedEvent>(HandleFinalStateEventAsync));
        _subscriptions.Add(_runtimeCoordinator.Subscribe<BackgroundTaskCancelledEvent>(HandleFinalStateEventAsync));
        _subscriptions.Add(_runtimeCoordinator.Subscribe<BackgroundTaskFaultedEvent>(HandleFinalStateEventAsync));

        _pumpTask = Task.Run(() => PumpAsync(_cts.Token), CancellationToken.None);
    }

    public void UpdateRunConfig(AgentRunConfig? runConfig)
    {
        if (runConfig is null)
            return;

        lock (_stateLock)
        {
            _runConfig = runConfig;
        }
    }

    private async ValueTask HandleFinalStateEventAsync(BackgroundTaskEvent evt)
    {
        if (!_finalStateEvents.Writer.TryWrite(evt))
        {
            await PublishSuppressedAsync(evt, Guid.NewGuid().ToString("N"), "notification-dispatcher-closed")
                .ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _finalStateEvents.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var events = new List<BackgroundTaskEvent>();
                while (_finalStateEvents.Reader.TryRead(out var evt))
                    events.Add(evt);

                if (events.Count == 0)
                    continue;

                if (_batchWindow > TimeSpan.Zero)
                {
                    await Task.Delay(_batchWindow, cancellationToken).ConfigureAwait(false);
                    while (_finalStateEvents.Reader.TryRead(out var evt))
                        events.Add(evt);
                }

                await FlushAsync(events, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FlushAsync(
        IReadOnlyList<BackgroundTaskEvent> events,
        CancellationToken cancellationToken)
    {
        var byScope = new Dictionary<(string SessionId, string ThreadId, string? BatchKey), List<(BackgroundTaskEvent Event, BackgroundTaskNotification Notification, string Reason)>>(
            capacity: events.Count);

        foreach (var evt in events)
        {
            var notificationId = Guid.NewGuid().ToString("N");

            var sessionId = evt.SessionId ?? evt.Invocation?.SessionId;
            var threadId = evt.ThreadId ?? evt.Invocation?.ThreadId;
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(threadId))
            {
                await PublishSuppressedAsync(evt, notificationId, "missing-thread-scope").ConfigureAwait(false);
                continue;
            }

            if (!TryReserveFinalStateNotification(evt.TaskId))
            {
                await PublishSuppressedAsync(evt, notificationId, "duplicate-final-state-notification").ConfigureAwait(false);
                continue;
            }

            var runConfig = GetCurrentRunConfig();
            var notificationContext = new BackgroundTaskNotificationContext
            {
                AgentId = _agentId,
                SessionId = sessionId!,
                ThreadId = threadId!,
                FinalStateStatus = GetFinalStateStatus(evt),
                RunConfig = runConfig
            };

            var (decision, reason) = await DecideAsync(evt, notificationContext, cancellationToken)
                .ConfigureAwait(false);

            if (decision is BackgroundTaskNotificationDecision.Suppress)
            {
                await PublishSuppressedAsync(evt, notificationId, reason).ConfigureAwait(false);
                continue;
            }

            var queue = (BackgroundTaskNotificationDecision.Queue)decision;
            var notification = new BackgroundTaskNotification(
                notificationId,
                [evt.TaskId],
                queue.Summary,
                queue.Metadata);

            var key = (sessionId!, threadId!, queue.BatchKey);
            if (!byScope.TryGetValue(key, out var scoped))
            {
                scoped = [];
                byScope.Add(key, scoped);
            }

            scoped.Add((evt, notification, reason));
        }

        foreach (var ((sessionId, threadId, _), scoped) in byScope)
        {
            foreach (var item in scoped)
            {
                _runtimeCoordinator.Emit(new BackgroundTaskNotificationQueuedEvent
                {
                    NotificationId = item.Notification.NotificationId,
                    TaskIds = item.Notification.TaskIds,
                    QueuedAt = DateTimeOffset.UtcNow,
                    Reason = item.Reason,
                    SessionId = sessionId,
                    ThreadId = threadId
                });
            }

            var input = new BackgroundTaskNotificationInputEvent(scoped.Select(item => item.Notification).ToList())
            {
                SessionId = sessionId,
                ThreadId = threadId,
                AgentId = _agentId,
                RunConfig = GetCurrentRunConfig(),
                RuntimeRunId = Guid.NewGuid().ToString("N")
            };

            if (!_runtimeWriter.TryWrite(input))
            {
                foreach (var item in scoped)
                    await PublishSuppressedAsync(item.Event, item.Notification.NotificationId, "runtime-input-closed")
                        .ConfigureAwait(false);
            }
        }
    }

    private AgentRunConfig? GetCurrentRunConfig()
    {
        lock (_stateLock)
            return _runConfig;
    }

    private bool TryReserveFinalStateNotification(string taskId)
    {
        lock (_stateLock)
            return _notifiedFinalStateTaskIds.Add(taskId);
    }

    private Task PublishSuppressedAsync(BackgroundTaskEvent evt, string notificationId, string reason)
    {
        _runtimeCoordinator.Emit(new BackgroundTaskNotificationSuppressedEvent
        {
            NotificationId = notificationId,
            TaskIds = [evt.TaskId],
            SuppressedAt = DateTimeOffset.UtcNow,
            Reason = reason,
            SessionId = evt.SessionId ?? evt.Invocation?.SessionId,
            ThreadId = evt.ThreadId ?? evt.Invocation?.ThreadId
        });
        return Task.CompletedTask;
    }

    public static UserMessagesInputEvent ToUserMessagesInput(BackgroundTaskNotificationInputEvent input)
        => new()
        {
            Messages = [
            new ChatMessage(ChatRole.System, FormatInput(input.Notifications))
                .WithPolicy(
                    AgentMessageSource.BackgroundNotification,
                    AgentMessageVisibility.Hidden,
                    AgentMessagePersistence.ModelContextOnly)
            ],
            ClientInputId = input.ClientInputId,
            SessionId = input.SessionId,
            ThreadId = input.ThreadId,
            AgentId = input.AgentId,
            RunConfig = input.RunConfig,
            RuntimeRunId = input.RuntimeRunId
        };

    private async ValueTask<(BackgroundTaskNotificationDecision Decision, string Reason)> DecideAsync(
        BackgroundTaskEvent evt,
        BackgroundTaskNotificationContext context,
        CancellationToken cancellationToken)
    {
        if (evt is BackgroundTaskCancelledEvent { Reason: "runtime-stopping" })
        {
            const string reason = "runtime-stopping-cancellation";
            return (new BackgroundTaskNotificationDecision.Suppress(reason), reason);
        }

        if (IsNotificationSuppressedByMetadata(evt.Metadata, out var metadataReason))
            return (new BackgroundTaskNotificationDecision.Suppress(metadataReason), metadataReason);

        return await EvaluateRuleAsync(evt, context, evt.Notification, cancellationToken)
            .ConfigureAwait(false);
    }

    private async ValueTask<(BackgroundTaskNotificationDecision Decision, string Reason)> EvaluateRuleAsync(
        BackgroundTaskEvent evt,
        BackgroundTaskNotificationContext context,
        BackgroundTaskNotificationRule rule,
        CancellationToken cancellationToken)
    {
        switch (rule)
        {
            case BackgroundTaskNotificationRule.NoneRule:
            {
                var reason = $"rule-suppressed:none:{context.FinalStateStatus}";
                return (new BackgroundTaskNotificationDecision.Suppress(reason), reason);
            }

            case BackgroundTaskNotificationRule.OnFinalStateRule onFinalState:
                return EvaluateOnFinalStateRule(evt, context, onFinalState);

            case BackgroundTaskNotificationRule.StrategyRule strategyRule:
                return await EvaluateStrategyRuleAsync(evt, context, strategyRule, cancellationToken)
                    .ConfigureAwait(false);

            default:
            {
                var reason = $"rule-suppressed:unknown:{context.FinalStateStatus}";
                return (new BackgroundTaskNotificationDecision.Suppress(reason), reason);
            }
        }
    }

    private static (BackgroundTaskNotificationDecision Decision, string Reason) EvaluateOnFinalStateRule(
        BackgroundTaskEvent evt,
        BackgroundTaskNotificationContext context,
        BackgroundTaskNotificationRule.OnFinalStateRule rule)
    {
        var shouldQueue = context.FinalStateStatus switch
        {
            "completed" => rule.Completed,
            "cancelled" => rule.Cancelled,
            "faulted" => rule.Faulted,
            _ => false
        };

        if (!shouldQueue)
        {
            var reason = $"rule-suppressed:on-final-state:{context.FinalStateStatus}";
            return (new BackgroundTaskNotificationDecision.Suppress(reason), reason);
        }

        return (new BackgroundTaskNotificationDecision.Queue(
                FormatSummary(evt),
                CreateMetadata(evt)),
            $"rule-queued:on-final-state:{context.FinalStateStatus}");
    }

    private async ValueTask<(BackgroundTaskNotificationDecision Decision, string Reason)> EvaluateStrategyRuleAsync(
        BackgroundTaskEvent evt,
        BackgroundTaskNotificationContext context,
        BackgroundTaskNotificationRule.StrategyRule rule,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            var reason = $"strategy-not-found:{rule.Name}";
            return (new BackgroundTaskNotificationDecision.Suppress(reason), reason);
        }

        if (_strategyRegistry?.TryGetStrategy(rule.Name, out var strategy) != true)
        {
            if (rule.Fallback is not null)
            {
                return await EvaluateRuleAsync(evt, context, rule.Fallback, cancellationToken)
                    .ConfigureAwait(false);
            }

            var reason = $"strategy-not-found:{rule.Name}";
            return (new BackgroundTaskNotificationDecision.Suppress(reason), reason);
        }

        try
        {
            var decision = await strategy.DecideAsync(evt, context, cancellationToken)
                .ConfigureAwait(false);

            return decision switch
            {
                BackgroundTaskNotificationDecision.Queue =>
                    (decision, $"strategy-queued:{rule.Name}:{context.FinalStateStatus}"),
                BackgroundTaskNotificationDecision.Suppress suppress =>
                    (suppress, $"strategy-suppressed:{rule.Name}:{suppress.Reason}"),
                _ =>
                    (new BackgroundTaskNotificationDecision.Suppress($"strategy-suppressed:{rule.Name}:unknown-decision"),
                        $"strategy-suppressed:{rule.Name}:unknown-decision")
            };
        }
        catch (Exception ex)
        {
            var reason = $"strategy-faulted:{rule.Name}:{ex.GetType().FullName ?? ex.GetType().Name}";
            return (new BackgroundTaskNotificationDecision.Suppress(reason), reason);
        }
    }

    private static bool IsNotificationSuppressedByMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        out string reason)
    {
        reason = "";
        if (metadata?.TryGetValue(BackgroundTaskNotificationMetadataKeys.SuppressNotification, out var suppress) != true ||
            !bool.TryParse(suppress, out var shouldSuppress) ||
            !shouldSuppress)
        {
            return false;
        }

        reason = metadata.TryGetValue(BackgroundTaskNotificationMetadataKeys.SuppressNotificationReason, out var metadataReason) &&
            !string.IsNullOrWhiteSpace(metadataReason)
                ? metadataReason
                : "metadata-suppressed";
        return true;
    }

    private static string GetFinalStateStatus(BackgroundTaskEvent evt) =>
        evt switch
        {
            BackgroundTaskCompletedEvent => "completed",
            BackgroundTaskCancelledEvent => "cancelled",
            BackgroundTaskFaultedEvent => "faulted",
            _ => "final-state"
        };

    private static string FormatSummary(BackgroundTaskEvent evt) =>
        evt switch
        {
            BackgroundTaskCompletedEvent completed when !string.IsNullOrWhiteSpace(completed.Summary) =>
                completed.Summary!,
            BackgroundTaskCompletedEvent =>
                $"Background task '{evt.Name}' completed.",
            BackgroundTaskCancelledEvent cancelled =>
                string.IsNullOrWhiteSpace(cancelled.Reason)
                    ? $"Background task '{evt.Name}' was cancelled."
                    : $"Background task '{evt.Name}' was cancelled: {cancelled.Reason}.",
            BackgroundTaskFaultedEvent faulted =>
                $"Background task '{evt.Name}' failed: {faulted.ErrorMessage}",
            _ =>
                $"Background task '{evt.Name}' reached a final state."
        };

    private static IReadOnlyDictionary<string, string> CreateMetadata(BackgroundTaskEvent evt)
    {
        var metadata = new Dictionary<string, string>
        {
            ["taskName"] = evt.Name,
            ["sourceKind"] = evt.SourceKind.ToString(),
            ["status"] = GetFinalStateStatus(evt),
            ["notificationRule"] = DescribeNotificationRule(evt.Notification)
        };

        if (!string.IsNullOrWhiteSpace(evt.SourceId))
            metadata["sourceId"] = evt.SourceId!;
        if (!string.IsNullOrWhiteSpace(evt.ParentRuntimeRunId))
            metadata["parentRuntimeRunId"] = evt.ParentRuntimeRunId!;

        if (evt.Metadata is { Count: > 0 })
        {
            foreach (var (key, value) in evt.Metadata)
                metadata[$"task.{key}"] = value;
        }

        if (evt is BackgroundTaskFaultedEvent faulted)
        {
            metadata["exceptionType"] = faulted.ExceptionType;
            metadata["errorMessage"] = faulted.ErrorMessage;
        }

        return metadata;
    }

    private static string DescribeNotificationRule(BackgroundTaskNotificationRule rule) =>
        rule switch
        {
            BackgroundTaskNotificationRule.NoneRule => "none",
            BackgroundTaskNotificationRule.OnFinalStateRule onFinalState =>
                $"on_final_state:completed={onFinalState.Completed.ToString().ToLowerInvariant()};faulted={onFinalState.Faulted.ToString().ToLowerInvariant()};cancelled={onFinalState.Cancelled.ToString().ToLowerInvariant()}",
            BackgroundTaskNotificationRule.StrategyRule strategy =>
                $"strategy:{strategy.Name}",
            _ => "unknown"
        };

    private static string FormatInput(IReadOnlyList<BackgroundTaskNotification> notifications)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("<background-task-notifications>");
        foreach (var notification in notifications)
        {
            builder.Append("  <notification id=\"")
                .Append(EscapeXmlAttribute(notification.NotificationId))
                .AppendLine("\">");
            builder.Append("    <summary>")
                .Append(EscapeXmlText(notification.Summary))
                .AppendLine("</summary>");
            builder.AppendLine("    <task-ids>");
            foreach (var taskId in notification.TaskIds)
            {
                builder.Append("      <task-id>")
                    .Append(EscapeXmlText(taskId))
                    .AppendLine("</task-id>");
            }
            builder.AppendLine("    </task-ids>");

            if (notification.Metadata is { Count: > 0 })
            {
                builder.AppendLine("    <metadata>");
                foreach (var (key, value) in notification.Metadata)
                {
                    builder.Append("      <entry key=\"")
                        .Append(EscapeXmlAttribute(key))
                        .Append("\">")
                        .Append(EscapeXmlText(value))
                        .AppendLine("</entry>");
                }
                builder.AppendLine("    </metadata>");
            }

            builder.AppendLine("  </notification>");
        }
        builder.Append("</background-task-notifications>");
        return builder.ToString();
    }

    private static string EscapeXmlText(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string EscapeXmlAttribute(string value) =>
        EscapeXmlText(value)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);

    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (!TryBeginDispose())
            return;

        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        _finalStateEvents.Writer.TryComplete();

        try
        {
            await _pumpTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _cts.Cancel();
            try
            {
                await _pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            _cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (!TryBeginDispose())
            return;

        foreach (var subscription in _subscriptions)
            subscription.Dispose();

        _finalStateEvents.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }

    private bool TryBeginDispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return false;

            _disposed = true;
            return true;
        }
    }
}
