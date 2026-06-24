using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

internal sealed class BackgroundTaskNotificationDispatcher : IDisposable
{
    private static readonly TimeSpan DefaultBatchWindow = TimeSpan.FromMilliseconds(25);

    private readonly string _agentId;
    private readonly HPD.Events.IEventCoordinator _runtimeCoordinator;
    private readonly ChannelWriter<AgentInputEvent> _runtimeWriter;
    private readonly Func<AgentEvent, Task> _publishControlEventAsync;
    private readonly Channel<BackgroundTaskEvent> _terminalEvents;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly object _stateLock = new();
    private readonly HashSet<string> _queuedTerminalTaskIds = new(StringComparer.Ordinal);
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
        Func<AgentEvent, Task> publishControlEventAsync,
        TimeSpan? batchWindow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(runtimeCoordinator);
        ArgumentNullException.ThrowIfNull(runtimeWriter);
        ArgumentNullException.ThrowIfNull(publishControlEventAsync);

        _agentId = agentId;
        _runtimeCoordinator = runtimeCoordinator;
        _runtimeWriter = runtimeWriter;
        _runConfig = runConfig;
        _publishControlEventAsync = publishControlEventAsync;
        _batchWindow = batchWindow ?? DefaultBatchWindow;
        _terminalEvents = Channel.CreateUnbounded<BackgroundTaskEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

        _subscriptions.Add(_runtimeCoordinator.Subscribe<BackgroundTaskCompletedEvent>(HandleTerminalEventAsync));
        _subscriptions.Add(_runtimeCoordinator.Subscribe<BackgroundTaskCancelledEvent>(HandleTerminalEventAsync));
        _subscriptions.Add(_runtimeCoordinator.Subscribe<BackgroundTaskFaultedEvent>(HandleTerminalEventAsync));

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

    private async ValueTask HandleTerminalEventAsync(BackgroundTaskEvent evt)
    {
        if (!_terminalEvents.Writer.TryWrite(evt))
        {
            await PublishSuppressedAsync(evt, Guid.NewGuid().ToString("N"), "notification-dispatcher-closed")
                .ConfigureAwait(false);
        }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _terminalEvents.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var events = new List<BackgroundTaskEvent>();
                while (_terminalEvents.Reader.TryRead(out var evt))
                    events.Add(evt);

                if (events.Count == 0)
                    continue;

                if (_batchWindow > TimeSpan.Zero)
                {
                    await Task.Delay(_batchWindow, cancellationToken).ConfigureAwait(false);
                    while (_terminalEvents.Reader.TryRead(out var evt))
                        events.Add(evt);
                }

                await FlushAsync(events).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task FlushAsync(IReadOnlyList<BackgroundTaskEvent> events)
    {
        var byScope = new Dictionary<(string SessionId, string ThreadId), List<(BackgroundTaskEvent Event, BackgroundTaskNotification Notification, string Reason)>>(
            capacity: events.Count);

        foreach (var evt in events)
        {
            var notificationId = Guid.NewGuid().ToString("N");

            if (!ShouldQueue(evt, out var reason))
            {
                await PublishSuppressedAsync(evt, notificationId, reason).ConfigureAwait(false);
                continue;
            }

            var sessionId = evt.SessionId ?? evt.Invocation?.SessionId;
            var threadId = evt.ThreadId ?? evt.Invocation?.ThreadId;
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(threadId))
            {
                await PublishSuppressedAsync(evt, notificationId, "missing-thread-scope").ConfigureAwait(false);
                continue;
            }

            if (!TryReserveTerminalNotification(evt.TaskId))
            {
                await PublishSuppressedAsync(evt, notificationId, "duplicate-terminal-notification").ConfigureAwait(false);
                continue;
            }

            var notification = new BackgroundTaskNotification(
                notificationId,
                [evt.TaskId],
                FormatSummary(evt),
                CreateMetadata(evt));

            var key = (sessionId!, threadId!);
            if (!byScope.TryGetValue(key, out var scoped))
            {
                scoped = [];
                byScope.Add(key, scoped);
            }

            scoped.Add((evt, notification, reason));
        }

        foreach (var ((sessionId, threadId), scoped) in byScope)
        {
            foreach (var item in scoped)
            {
                await _publishControlEventAsync(new BackgroundTaskNotificationQueuedEvent
                {
                    NotificationId = item.Notification.NotificationId,
                    TaskIds = item.Notification.TaskIds,
                    QueuedAt = DateTimeOffset.UtcNow,
                    Reason = item.Reason,
                    SessionId = sessionId,
                    ThreadId = threadId
                }).ConfigureAwait(false);
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

    private bool TryReserveTerminalNotification(string taskId)
    {
        lock (_stateLock)
            return _queuedTerminalTaskIds.Add(taskId);
    }

    private async Task PublishSuppressedAsync(BackgroundTaskEvent evt, string notificationId, string reason)
    {
        await _publishControlEventAsync(new BackgroundTaskNotificationSuppressedEvent
        {
            NotificationId = notificationId,
            TaskIds = [evt.TaskId],
            SuppressedAt = DateTimeOffset.UtcNow,
            Reason = reason,
            SessionId = evt.SessionId ?? evt.Invocation?.SessionId,
            ThreadId = evt.ThreadId ?? evt.Invocation?.ThreadId
        }).ConfigureAwait(false);
    }

    public static UserMessagesInputEvent ToUserMessagesInput(BackgroundTaskNotificationInputEvent input)
        => new([new ChatMessage(ChatRole.System, FormatInput(input.Notifications))])
        {
            ClientInputId = input.ClientInputId,
            SessionId = input.SessionId,
            ThreadId = input.ThreadId,
            AgentId = input.AgentId,
            RunConfig = input.RunConfig,
            RuntimeRunId = input.RuntimeRunId
        };

    private static bool ShouldQueue(BackgroundTaskEvent evt, out string reason)
    {
        if (evt is BackgroundTaskCancelledEvent { Reason: "runtime-stopping" })
        {
            reason = "runtime-stopping-cancellation";
            return false;
        }

        var status = GetTerminalStatus(evt);
        var shouldQueue = evt.NotificationPolicy switch
        {
            BackgroundTaskNotificationPolicy.None => false,
            BackgroundTaskNotificationPolicy.OnFault => status == "faulted",
            BackgroundTaskNotificationPolicy.OnCompletion => status == "completed",
            BackgroundTaskNotificationPolicy.OnCompletionOrFault => status is "completed" or "faulted",
            BackgroundTaskNotificationPolicy.Custom => false,
            _ => false
        };

        reason = shouldQueue
            ? $"{evt.NotificationPolicy}:{status}"
            : $"policy-suppressed:{evt.NotificationPolicy}:{status}";
        return shouldQueue;
    }

    private static string GetTerminalStatus(BackgroundTaskEvent evt) =>
        evt switch
        {
            BackgroundTaskCompletedEvent => "completed",
            BackgroundTaskCancelledEvent => "cancelled",
            BackgroundTaskFaultedEvent => "faulted",
            _ => "terminal"
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
                $"Background task '{evt.Name}' reached a terminal state."
        };

    private static IReadOnlyDictionary<string, string> CreateMetadata(BackgroundTaskEvent evt)
    {
        var metadata = new Dictionary<string, string>
        {
            ["taskName"] = evt.Name,
            ["sourceKind"] = evt.SourceKind.ToString(),
            ["status"] = GetTerminalStatus(evt),
            ["notificationPolicy"] = evt.NotificationPolicy.ToString()
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

        _terminalEvents.Writer.TryComplete();

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

        _terminalEvents.Writer.TryComplete();
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
