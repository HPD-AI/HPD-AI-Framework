using HPD.Agent.Middleware;

namespace HPD.Agent;

public static class ThreadRunStatus
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";
}

public sealed record ThreadRunProjection(
    string RuntimeRunId,
    string AgentId,
    string SessionId,
    string ThreadId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    ThreadRunProjectionError? Error,
    ThreadRunProjectionModelBackgroundOperation? ModelBackgroundOperation,
    IReadOnlyList<ThreadRunProjectionBackgroundTask> BackgroundTasks,
    IReadOnlyList<ThreadRunProjectionBackgroundHandle> BackgroundHandles);

public sealed record ThreadRunProjectionError(
    string? Type,
    string? Message);

public sealed record ThreadRunProjectionModelBackgroundOperation(
    string Status,
    string? OperationId,
    string? StatusMessage,
    string? ContinuationToken);

public sealed record ThreadRunProjectionBackgroundTask(
    string TaskId,
    string Name,
    string SourceKind,
    string? SourceId,
    ThreadRunProjectionBackgroundTaskNotification Notification,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? FaultedAt,
    string? ErrorType,
    string? ErrorMessage);

/// <summary>
/// Readable projection of a background task notification rule.
/// </summary>
/// <param name="Kind">Rule kind, such as none, on_final_state, or strategy.</param>
/// <param name="StrategyName">Strategy name when <paramref name="Kind"/> is strategy.</param>
public sealed record ThreadRunProjectionBackgroundTaskNotification(
    string Kind,
    string? StrategyName = null);

public sealed record ThreadRunProjectionBackgroundHandle(
    string HandleId,
    string Name,
    string HandleKind,
    string SourceKind,
    string? SourceId,
    string Status,
    BackgroundHandleOperation SupportedOperations,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyDictionary<string, string>? Metadata);

public static class ThreadRunProjector
{
    public static IReadOnlyList<ThreadRunProjection> Project(
        string agentId,
        string sessionId,
        string threadId,
        IEnumerable<AgentEvent> events,
        string? activeRuntimeRunId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(events);

        var runs = new List<ThreadRunProjectionBuilder>();
        ThreadRunProjectionBuilder? current = null;

        foreach (var evt in events.OrderBy(evt => evt.SequenceNumber))
        {
            switch (evt)
            {
                case ThreadRunStartedEvent started when started.AgentId == agentId:
                    current = new ThreadRunProjectionBuilder(
                        started.RuntimeRunId,
                        started.AgentId,
                        sessionId,
                        threadId,
                        started.StartedAt);
                    runs.Add(current);
                    break;

                case ThreadRunCompletedEvent completed when completed.AgentId == agentId:
                    current = runs.LastOrDefault(run => run.RuntimeRunId == completed.RuntimeRunId);
                    current?.Complete(completed);
                    break;

                case ModelBackgroundOperationStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetModelBackgroundOperation(started);
                    break;

                case ModelBackgroundOperationStatusEvent status:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetModelBackgroundOperationStatus(status);
                    break;

                case BackgroundTaskStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetTask(started.TaskId, started.Name, started.SourceKind, started.SourceId, started.Notification, "started", started.StartedAt);
                    break;

                case BackgroundTaskCompletedEvent completed:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetTask(completed.TaskId, completed.Name, completed.SourceKind, completed.SourceId, completed.Notification, "completed", completed.CompletedAt);
                    break;

                case BackgroundTaskCancelledEvent cancelled:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetTask(cancelled.TaskId, cancelled.Name, cancelled.SourceKind, cancelled.SourceId, cancelled.Notification, "cancelled", cancelled.CancelledAt);
                    break;

                case BackgroundTaskFaultedEvent faulted:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetTask(faulted.TaskId, faulted.Name, faulted.SourceKind, faulted.SourceId, faulted.Notification, "faulted", faulted.FaultedAt, faulted.ExceptionType, faulted.ErrorMessage);
                    break;

                case BackgroundHandleRegisteredEvent registered:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetHandle(registered);
                    break;

                case BackgroundHandleStatusChangedEvent changed:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetHandleStatus(changed);
                    break;
            }
        }

        foreach (var run in runs)
            run.MarkInterruptedIfNotLive(activeRuntimeRunId);

        return runs.Where(run => run.AgentId == agentId).Select(run => run.ToProjection()).ToList();
    }

#pragma warning disable MEAI001 // Experimental API - Background Responses
    private static string? SerializeToken(Microsoft.Extensions.AI.ResponseContinuationToken? token) =>
        token == null ? null : Convert.ToBase64String(token.ToBytes().Span);
#pragma warning restore MEAI001

    private sealed class ThreadRunProjectionBuilder
    {
        private readonly Dictionary<string, ThreadRunProjectionBackgroundTaskBuilder> _tasks = new();
        private readonly Dictionary<string, ThreadRunProjectionBackgroundHandleBuilder> _handles = new();

        public ThreadRunProjectionBuilder(
            string runtimeRunId,
            string agentId,
            string sessionId,
            string threadId,
            DateTimeOffset startedAt)
        {
            RuntimeRunId = runtimeRunId;
            AgentId = agentId;
            SessionId = sessionId;
            ThreadId = threadId;
            StartedAt = startedAt;
        }

        public string RuntimeRunId { get; }
        public string AgentId { get; }
        public string SessionId { get; }
        public string ThreadId { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public string Status { get; private set; } = ThreadRunStatus.Active;
        public ThreadRunProjectionError? Error { get; private set; }
        public ThreadRunProjectionModelBackgroundOperation? ModelBackgroundOperation { get; private set; }

        public void Complete(ThreadRunCompletedEvent completed)
        {
            CompletedAt = completed.Timestamp;
            Status = completed.ErrorType != null
                ? ThreadRunStatus.Failed
                : completed.Cancelled ? ThreadRunStatus.Cancelled : ThreadRunStatus.Completed;
            Error = completed.ErrorType == null && completed.ErrorMessage == null
                ? null
                : new ThreadRunProjectionError(completed.ErrorType, completed.ErrorMessage);
        }

        public void MarkInterruptedIfNotLive(string? activeRuntimeRunId)
        {
            if (Status == ThreadRunStatus.Active && RuntimeRunId != activeRuntimeRunId)
                Status = ThreadRunStatus.Interrupted;
        }

        public void SetModelBackgroundOperation(ModelBackgroundOperationStartedEvent evt)
        {
            ModelBackgroundOperation = new ThreadRunProjectionModelBackgroundOperation(
                evt.Status.Value,
                evt.OperationId,
                null,
                SerializeToken(evt.ContinuationToken));
        }

        public void SetModelBackgroundOperationStatus(ModelBackgroundOperationStatusEvent evt)
        {
            ModelBackgroundOperation = new ThreadRunProjectionModelBackgroundOperation(
                evt.Status.Value,
                ModelBackgroundOperation?.OperationId,
                evt.StatusMessage,
                SerializeToken(evt.ContinuationToken));
        }

        public void SetTask(
            string taskId,
            string name,
            BackgroundTaskSourceKind sourceKind,
            string? sourceId,
            BackgroundTaskNotificationRule notification,
            string status,
            DateTimeOffset timestamp,
            string? errorType = null,
            string? errorMessage = null)
        {
            if (!_tasks.TryGetValue(taskId, out var task))
            {
                task = new ThreadRunProjectionBackgroundTaskBuilder(taskId, name, sourceKind, sourceId, notification);
                _tasks[taskId] = task;
            }

            task.SourceKind = sourceKind;
            task.SourceId = sourceId;
            task.Notification = notification;
            task.Status = status;
            task.ErrorType = errorType;
            task.ErrorMessage = errorMessage;
            task.SetTimestamp(status, timestamp);
        }

        public void SetHandle(BackgroundHandleRegisteredEvent evt)
        {
            var handle = new ThreadRunProjectionBackgroundHandleBuilder(
                evt.HandleId,
                evt.Name,
                evt.HandleKind,
                evt.SourceKind,
                evt.SourceId,
                evt.SupportedOperations,
                evt.RegisteredAt,
                evt.Metadata);
            _handles[evt.HandleId] = handle;
        }

        public void SetHandleStatus(BackgroundHandleStatusChangedEvent evt)
        {
            if (!_handles.TryGetValue(evt.HandleId, out var handle))
            {
                handle = new ThreadRunProjectionBackgroundHandleBuilder(
                    evt.HandleId,
                    evt.HandleId,
                    BackgroundHandleKind.Other,
                    BackgroundTaskSourceKind.Other,
                    null,
                    BackgroundHandleOperation.Status,
                    evt.ObservedAt,
                    evt.Metadata);
                _handles[evt.HandleId] = handle;
            }

            handle.Status = evt.Status;
            handle.UpdatedAt = evt.ObservedAt;
            handle.Metadata = evt.Metadata ?? handle.Metadata;
        }

        public ThreadRunProjection ToProjection() =>
            new(
                RuntimeRunId,
                AgentId,
                SessionId,
                ThreadId,
                Status,
                StartedAt,
                CompletedAt,
                Error,
                ModelBackgroundOperation,
                _tasks.Values.Select(task => task.ToProjection()).ToList(),
                _handles.Values.Select(handle => handle.ToProjection()).ToList());
    }

    private sealed class ThreadRunProjectionBackgroundTaskBuilder
    {
        public ThreadRunProjectionBackgroundTaskBuilder(string taskId, string name)
            : this(taskId, name, BackgroundTaskSourceKind.Other, null, BackgroundTaskNotificationRule.None)
        {
        }

        public ThreadRunProjectionBackgroundTaskBuilder(
            string taskId,
            string name,
            BackgroundTaskSourceKind sourceKind,
            string? sourceId,
            BackgroundTaskNotificationRule notification)
        {
            TaskId = taskId;
            Name = name;
            SourceKind = sourceKind;
            SourceId = sourceId;
            Notification = notification;
        }

        public string TaskId { get; }
        public string Name { get; }
        public BackgroundTaskSourceKind SourceKind { get; set; }
        public string? SourceId { get; set; }
        public BackgroundTaskNotificationRule Notification { get; set; }
        public string Status { get; set; } = "started";
        public DateTimeOffset? StartedAt { get; private set; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public DateTimeOffset? CancelledAt { get; private set; }
        public DateTimeOffset? FaultedAt { get; private set; }
        public string? ErrorType { get; set; }
        public string? ErrorMessage { get; set; }

        public void SetTimestamp(string status, DateTimeOffset timestamp)
        {
            if (status == "started")
                StartedAt = timestamp;
            else if (status == "completed")
                CompletedAt = timestamp;
            else if (status == "cancelled")
                CancelledAt = timestamp;
            else if (status == "faulted")
                FaultedAt = timestamp;
        }

        public ThreadRunProjectionBackgroundTask ToProjection() =>
            new(
                TaskId,
                Name,
                SourceKind.ToString(),
                SourceId,
                CreateNotificationProjection(Notification),
                Status,
                StartedAt,
                CompletedAt,
                CancelledAt,
                FaultedAt,
                ErrorType,
                ErrorMessage);

        private static ThreadRunProjectionBackgroundTaskNotification CreateNotificationProjection(
            BackgroundTaskNotificationRule notification) =>
            notification switch
            {
                BackgroundTaskNotificationRule.NoneRule =>
                    new ThreadRunProjectionBackgroundTaskNotification("none"),
                BackgroundTaskNotificationRule.OnFinalStateRule =>
                    new ThreadRunProjectionBackgroundTaskNotification("on_final_state"),
                BackgroundTaskNotificationRule.StrategyRule strategy =>
                    new ThreadRunProjectionBackgroundTaskNotification("strategy", strategy.Name),
                _ =>
                    new ThreadRunProjectionBackgroundTaskNotification("unknown")
            };
    }

    private sealed class ThreadRunProjectionBackgroundHandleBuilder
    {
        public ThreadRunProjectionBackgroundHandleBuilder(
            string handleId,
            string name,
            BackgroundHandleKind handleKind,
            BackgroundTaskSourceKind sourceKind,
            string? sourceId,
            BackgroundHandleOperation supportedOperations,
            DateTimeOffset registeredAt,
            IReadOnlyDictionary<string, string>? metadata)
        {
            HandleId = handleId;
            Name = name;
            HandleKind = handleKind;
            SourceKind = sourceKind;
            SourceId = sourceId;
            SupportedOperations = supportedOperations;
            RegisteredAt = registeredAt;
            UpdatedAt = registeredAt;
            Metadata = metadata;
        }

        public string HandleId { get; }
        public string Name { get; }
        public BackgroundHandleKind HandleKind { get; }
        public BackgroundTaskSourceKind SourceKind { get; }
        public string? SourceId { get; }
        public string Status { get; set; } = "registered";
        public BackgroundHandleOperation SupportedOperations { get; }
        public DateTimeOffset RegisteredAt { get; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public IReadOnlyDictionary<string, string>? Metadata { get; set; }

        public ThreadRunProjectionBackgroundHandle ToProjection() =>
            new(
                HandleId,
                Name,
                HandleKind.ToString(),
                SourceKind.ToString(),
                SourceId,
                Status,
                SupportedOperations,
                RegisteredAt,
                UpdatedAt,
                Metadata);
    }
}
