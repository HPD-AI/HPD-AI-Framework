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
    ThreadRunProjectionBackgroundOperation? BackgroundOperation,
    IReadOnlyList<ThreadRunProjectionBackgroundTask> BackgroundTasks);

public sealed record ThreadRunProjectionError(
    string? Type,
    string? Message);

public sealed record ThreadRunProjectionBackgroundOperation(
    string Status,
    string? OperationId,
    string? StatusMessage,
    string? ContinuationToken);

public sealed record ThreadRunProjectionBackgroundTask(
    string TaskId,
    string Name,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? FaultedAt,
    string? ErrorType,
    string? ErrorMessage);

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

                case BackgroundOperationStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetBackgroundOperation(started);
                    break;

                case BackgroundOperationStatusEvent status:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetBackgroundOperationStatus(status);
                    break;

                case ToolCallBackgroundTaskStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetTask(started.TaskId, started.Name, "started", started.StartedAt);
                    break;

                case ToolCallBackgroundTaskCompletedEvent completed:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetTask(completed.TaskId, completed.Name, "completed", completed.CompletedAt);
                    break;

                case ToolCallBackgroundTaskCancelledEvent cancelled:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetTask(cancelled.TaskId, cancelled.Name, "cancelled", cancelled.CancelledAt);
                    break;

                case ToolCallBackgroundTaskFaultedEvent faulted:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatus.Active);
                    current?.SetTask(faulted.TaskId, faulted.Name, "faulted", faulted.FaultedAt, faulted.ExceptionType, faulted.ErrorMessage);
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
        public ThreadRunProjectionBackgroundOperation? BackgroundOperation { get; private set; }

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

        public void SetBackgroundOperation(BackgroundOperationStartedEvent evt)
        {
            BackgroundOperation = new ThreadRunProjectionBackgroundOperation(
                evt.Status.Value,
                evt.OperationId,
                null,
                SerializeToken(evt.ContinuationToken));
        }

        public void SetBackgroundOperationStatus(BackgroundOperationStatusEvent evt)
        {
            BackgroundOperation = new ThreadRunProjectionBackgroundOperation(
                evt.Status.Value,
                BackgroundOperation?.OperationId,
                evt.StatusMessage,
                SerializeToken(evt.ContinuationToken));
        }

        public void SetTask(
            string taskId,
            string name,
            string status,
            DateTimeOffset timestamp,
            string? errorType = null,
            string? errorMessage = null)
        {
            if (!_tasks.TryGetValue(taskId, out var task))
            {
                task = new ThreadRunProjectionBackgroundTaskBuilder(taskId, name);
                _tasks[taskId] = task;
            }

            task.Status = status;
            task.ErrorType = errorType;
            task.ErrorMessage = errorMessage;
            task.SetTimestamp(status, timestamp);
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
                BackgroundOperation,
                _tasks.Values.Select(task => task.ToProjection()).ToList());
    }

    private sealed class ThreadRunProjectionBackgroundTaskBuilder
    {
        public ThreadRunProjectionBackgroundTaskBuilder(string taskId, string name)
        {
            TaskId = taskId;
            Name = name;
        }

        public string TaskId { get; }
        public string Name { get; }
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
            new(TaskId, Name, Status, StartedAt, CompletedAt, CancelledAt, FaultedAt, ErrorType, ErrorMessage);
    }
}
