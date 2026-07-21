using HPD.Agent.Middleware;

namespace HPD.Agent;

public static class ThreadExecutionStatus
{
    public const string Active = "active";
    public const string Succeeded = "succeeded";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
    public const string Interrupted = "interrupted";
}

public sealed record ThreadExecutionProjection(
    string ThreadExecutionId,
    string AgentId,
    string SessionId,
    string ThreadId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ThreadExecutionProjectionError? Error,
    ThreadExecutionProjectionModelBackgroundOperation? ModelBackgroundOperation,
    IReadOnlyList<ThreadExecutionProjectionBackgroundTask> BackgroundTasks,
    IReadOnlyList<ThreadExecutionProjectionBackgroundHandle> BackgroundHandles);

public sealed record ThreadExecutionProjectionError(
    string? Type,
    string? Message);

public sealed record ThreadExecutionProjectionModelBackgroundOperation(
    string Status,
    string? OperationId,
    string? StatusMessage,
    string? ContinuationToken);

public sealed record ThreadExecutionProjectionBackgroundTask(
    string TaskId,
    string Name,
    string SourceKind,
    string? SourceId,
    ThreadExecutionProjectionBackgroundTaskNotification Notification,
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
public sealed record ThreadExecutionProjectionBackgroundTaskNotification(
    string Kind,
    string? StrategyName = null);

public sealed record ThreadExecutionProjectionBackgroundHandle(
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

public static class ThreadExecutionProjector
{
    public static bool IsProjectionEvent(AgentEvent evt) => evt is
        ThreadExecutionStartedEvent or
        ThreadExecutionFinishedEvent or
        ModelBackgroundOperationStartedEvent or
        ModelBackgroundOperationStatusEvent or
        BackgroundTaskStartedEvent or
        BackgroundTaskCompletedEvent or
        BackgroundTaskCancelledEvent or
        BackgroundTaskFaultedEvent or
        BackgroundHandleRegisteredEvent or
        BackgroundHandleStatusChangedEvent;

    public static IReadOnlyList<ThreadExecutionProjection> Project(
        string agentId,
        string sessionId,
        string threadId,
        IEnumerable<AgentEvent> events,
        string? activeThreadExecutionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentNullException.ThrowIfNull(events);

        var runs = new List<ThreadExecutionProjectionBuilder>();
        ThreadExecutionProjectionBuilder? current = null;

        foreach (var evt in events.OrderBy(evt => evt.ThreadSequenceNumber))
        {
            switch (evt)
            {
                case ThreadExecutionStartedEvent started when started.AgentId == agentId:
                    current = new ThreadExecutionProjectionBuilder(
                        started.ThreadExecutionId,
                        started.AgentId,
                        sessionId,
                        threadId,
                        started.StartedAt);
                    runs.Add(current);
                    break;

                case ThreadExecutionFinishedEvent completed when completed.AgentId == agentId:
                    current = runs.LastOrDefault(run => run.ThreadExecutionId == completed.ThreadExecutionId);
                    current?.Complete(completed);
                    break;

                case ModelBackgroundOperationStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);
                    current?.SetModelBackgroundOperation(started);
                    break;

                case ModelBackgroundOperationStatusEvent status:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);
                    current?.SetModelBackgroundOperationStatus(status);
                    break;

                case BackgroundTaskStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);
                    current?.SetTask(started.TaskId, started.Name, started.SourceKind, started.SourceId, started.Notification, "started", started.StartedAt);
                    break;

                case BackgroundTaskCompletedEvent completed:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);
                    current?.SetTask(completed.TaskId, completed.Name, completed.SourceKind, completed.SourceId, completed.Notification, "completed", completed.CompletedAt);
                    break;

                case BackgroundTaskCancelledEvent cancelled:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);
                    current?.SetTask(cancelled.TaskId, cancelled.Name, cancelled.SourceKind, cancelled.SourceId, cancelled.Notification, "cancelled", cancelled.CancelledAt);
                    break;

                case BackgroundTaskFaultedEvent faulted:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);
                    current?.SetTask(faulted.TaskId, faulted.Name, faulted.SourceKind, faulted.SourceId, faulted.Notification, "faulted", faulted.FaultedAt, faulted.ExceptionType, faulted.ErrorMessage);
                    break;

                case BackgroundHandleRegisteredEvent registered:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);
                    current?.SetHandle(registered);
                    break;

                case BackgroundHandleStatusChangedEvent changed:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadExecutionStatus.Active);
                    current?.SetHandleStatus(changed);
                    break;
            }
        }

        foreach (var run in runs)
            run.MarkInterruptedIfNotLive(activeThreadExecutionId);

        return runs.Where(run => run.AgentId == agentId).Select(run => run.ToProjection()).ToList();
    }

#pragma warning disable MEAI001 // Experimental API - Background Responses
    private static string? SerializeToken(Microsoft.Extensions.AI.ResponseContinuationToken? token) =>
        token == null ? null : Convert.ToBase64String(token.ToBytes().Span);
#pragma warning restore MEAI001

    private sealed class ThreadExecutionProjectionBuilder
    {
        private readonly Dictionary<string, ThreadExecutionProjectionBackgroundTaskBuilder> _tasks = new();
        private readonly Dictionary<string, ThreadExecutionProjectionBackgroundHandleBuilder> _handles = new();

        public ThreadExecutionProjectionBuilder(
            string threadExecutionId,
            string agentId,
            string sessionId,
            string threadId,
            DateTimeOffset startedAt)
        {
            ThreadExecutionId = threadExecutionId;
            AgentId = agentId;
            SessionId = sessionId;
            ThreadId = threadId;
            StartedAt = startedAt;
        }

        public string ThreadExecutionId { get; }
        public string AgentId { get; }
        public string SessionId { get; }
        public string ThreadId { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? FinishedAt { get; private set; }
        public string Status { get; private set; } = ThreadExecutionStatus.Active;
        public ThreadExecutionProjectionError? Error { get; private set; }
        public ThreadExecutionProjectionModelBackgroundOperation? ModelBackgroundOperation { get; private set; }

        public void Complete(ThreadExecutionFinishedEvent completed)
        {
            FinishedAt = completed.FinishedAt;
            Status = completed.Outcome switch
            {
                ThreadExecutionOutcome.Failed => ThreadExecutionStatus.Failed,
                ThreadExecutionOutcome.Cancelled => ThreadExecutionStatus.Cancelled,
                _ => ThreadExecutionStatus.Succeeded
            };
            Error = completed.Error is null
                ? null
                : new ThreadExecutionProjectionError(completed.Error.Type, completed.Error.Message);
        }

        public void MarkInterruptedIfNotLive(string? activeThreadExecutionId)
        {
            if (Status == ThreadExecutionStatus.Active && ThreadExecutionId != activeThreadExecutionId)
                Status = ThreadExecutionStatus.Interrupted;
        }

        public void SetModelBackgroundOperation(ModelBackgroundOperationStartedEvent evt)
        {
            ModelBackgroundOperation = new ThreadExecutionProjectionModelBackgroundOperation(
                evt.Status.Value,
                evt.OperationId,
                null,
                SerializeToken(evt.ContinuationToken));
        }

        public void SetModelBackgroundOperationStatus(ModelBackgroundOperationStatusEvent evt)
        {
            ModelBackgroundOperation = new ThreadExecutionProjectionModelBackgroundOperation(
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
                task = new ThreadExecutionProjectionBackgroundTaskBuilder(taskId, name, sourceKind, sourceId, notification);
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
            var handle = new ThreadExecutionProjectionBackgroundHandleBuilder(
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
                handle = new ThreadExecutionProjectionBackgroundHandleBuilder(
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

        public ThreadExecutionProjection ToProjection() =>
            new(
                ThreadExecutionId,
                AgentId,
                SessionId,
                ThreadId,
                Status,
                StartedAt,
                FinishedAt,
                Error,
                ModelBackgroundOperation,
                _tasks.Values.Select(task => task.ToProjection()).ToList(),
                _handles.Values.Select(handle => handle.ToProjection()).ToList());
    }

    private sealed class ThreadExecutionProjectionBackgroundTaskBuilder
    {
        public ThreadExecutionProjectionBackgroundTaskBuilder(string taskId, string name)
            : this(taskId, name, BackgroundTaskSourceKind.Other, null, BackgroundTaskNotificationRule.None)
        {
        }

        public ThreadExecutionProjectionBackgroundTaskBuilder(
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

        public ThreadExecutionProjectionBackgroundTask ToProjection() =>
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

        private static ThreadExecutionProjectionBackgroundTaskNotification CreateNotificationProjection(
            BackgroundTaskNotificationRule notification) =>
            notification switch
            {
                BackgroundTaskNotificationRule.NoneRule =>
                    new ThreadExecutionProjectionBackgroundTaskNotification("none"),
                BackgroundTaskNotificationRule.OnFinalStateRule =>
                    new ThreadExecutionProjectionBackgroundTaskNotification("on_final_state"),
                BackgroundTaskNotificationRule.StrategyRule strategy =>
                    new ThreadExecutionProjectionBackgroundTaskNotification("strategy", strategy.Name),
                _ =>
                    new ThreadExecutionProjectionBackgroundTaskNotification("unknown")
            };
    }

    private sealed class ThreadExecutionProjectionBackgroundHandleBuilder
    {
        public ThreadExecutionProjectionBackgroundHandleBuilder(
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

        public ThreadExecutionProjectionBackgroundHandle ToProjection() =>
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
