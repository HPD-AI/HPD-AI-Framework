using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentThreadRunService : IAgentThreadRunService
{
    private readonly SessionManager _sessionManager;

    public AgentThreadRunService(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<AgentServiceResult<IReadOnlyList<ThreadRunDto>>> ListRunsAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var runs = await LoadProjectedRunsAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);

        return runs == null
            ? AgentServiceResult<IReadOnlyList<ThreadRunDto>>.NotFound
            : AgentServiceResult<IReadOnlyList<ThreadRunDto>>.Success(runs);
    }

    public async Task<AgentServiceResult<ThreadRunDto?>> GetActiveRunAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var runs = await LoadProjectedRunsAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (runs == null)
            return AgentServiceResult<ThreadRunDto?>.NotFound;

        return AgentServiceResult<ThreadRunDto?>.Success(
            runs.LastOrDefault(run => run.Status == ThreadRunStatuses.Active));
    }

    public async Task<AgentServiceResult<ThreadRunDto>> GetRunAsync(
        string agentId,
        string sessionId,
        string threadId,
        string runtimeRunId,
        CancellationToken cancellationToken = default)
    {
        var runs = await LoadProjectedRunsAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (runs == null)
            return AgentServiceResult<ThreadRunDto>.NotFound;

        var run = runs.FirstOrDefault(candidate => candidate.RuntimeRunId == runtimeRunId);
        return run == null
            ? AgentServiceResult<ThreadRunDto>.NotFound
            : AgentServiceResult<ThreadRunDto>.Success(run);
    }

    private async Task<IReadOnlyList<ThreadRunDto>?> LoadProjectedRunsAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        var document = await _sessionManager.Store.LoadThreadDocumentAsync(sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (document == null && await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken)
                .ConfigureAwait(false) == null)
            return null;

        var events = document?.Events.OrderBy(evt => evt.SequenceNumber).ToList() ?? [];
        var runs = ProjectRuns(agentId, sessionId, threadId, events);
        MergeActiveRun(agentId, sessionId, threadId, runs);

        return runs.Select(run => run.ToDto()).ToList();
    }

    private void MergeActiveRun(
        string agentId,
        string sessionId,
        string threadId,
        List<ThreadRunBuilder> runs)
    {
        var active = _sessionManager.GetActiveThreadRun(sessionId, threadId);
        if (active == null || active.AgentId != agentId)
            return;

        if (runs.Any(run => run.RuntimeRunId == active.RuntimeRunId))
            return;

        runs.Add(new ThreadRunBuilder(
            active.RuntimeRunId,
            active.AgentId,
            active.SessionId,
            active.ThreadId,
            active.StartedAt));
    }

    private static List<ThreadRunBuilder> ProjectRuns(
        string agentId,
        string sessionId,
        string threadId,
        IReadOnlyList<AgentEvent> events)
    {
        var runs = new List<ThreadRunBuilder>();
        ThreadRunBuilder? current = null;

        foreach (var evt in events)
        {
            switch (evt)
            {
                case ThreadRunStartedEvent started when started.AgentId == agentId:
                    current = new ThreadRunBuilder(
                        started.RuntimeRunId,
                        started.AgentId,
                        sessionId,
                        threadId,
                        started.StartedAt);
                    runs.Add(current);
                    break;

                case ThreadRunCompletedEvent completed when completed.AgentId == agentId:
                    current = runs.LastOrDefault(run => run.RuntimeRunId == completed.RuntimeRunId);
                    if (current != null)
                        current.Complete(completed);
                    break;

                case BackgroundOperationStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatuses.Active);
                    current?.SetBackgroundOperation(started);
                    break;

                case BackgroundOperationStatusEvent status:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatuses.Active);
                    current?.SetBackgroundOperationStatus(status);
                    break;

                case ToolCallBackgroundTaskStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatuses.Active);
                    current?.SetTask(started.TaskId, started.Name, "started", started.StartedAt);
                    break;

                case ToolCallBackgroundTaskCompletedEvent completed:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatuses.Active);
                    current?.SetTask(completed.TaskId, completed.Name, "completed", completed.CompletedAt);
                    break;

                case ToolCallBackgroundTaskCancelledEvent cancelled:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatuses.Active);
                    current?.SetTask(cancelled.TaskId, cancelled.Name, "cancelled", cancelled.CancelledAt);
                    break;

                case ToolCallBackgroundTaskFaultedEvent faulted:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == ThreadRunStatuses.Active);
                    current?.SetTask(faulted.TaskId, faulted.Name, "faulted", faulted.FaultedAt, faulted.ExceptionType, faulted.ErrorMessage);
                    break;
            }
        }

        return runs.Where(run => run.AgentId == agentId).ToList();
    }

#pragma warning disable MEAI001 // Experimental API - Background Responses
    private static string? SerializeToken(Microsoft.Extensions.AI.ResponseContinuationToken? token) =>
        token == null ? null : Convert.ToBase64String(token.ToBytes().Span);
#pragma warning restore MEAI001

    private sealed class ThreadRunBuilder
    {
        private readonly Dictionary<string, ThreadRunBackgroundTaskBuilder> _tasks = new();

        public ThreadRunBuilder(
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
        public string Status { get; private set; } = ThreadRunStatuses.Active;
        public ThreadRunErrorDto? Error { get; private set; }
        public ThreadRunBackgroundOperationDto? BackgroundOperation { get; private set; }

        public void Complete(ThreadRunCompletedEvent completed)
        {
            CompletedAt = completed.Timestamp;
            Status = completed.ErrorType != null
                ? ThreadRunStatuses.Failed
                : completed.Cancelled ? ThreadRunStatuses.Cancelled : ThreadRunStatuses.Completed;
            Error = completed.ErrorType == null && completed.ErrorMessage == null
                ? null
                : new ThreadRunErrorDto(completed.ErrorType, completed.ErrorMessage);
        }

        public void SetBackgroundOperation(BackgroundOperationStartedEvent evt)
        {
            BackgroundOperation = new ThreadRunBackgroundOperationDto(
                evt.Status.Value,
                evt.OperationId,
                null,
                SerializeToken(evt.ContinuationToken));
        }

        public void SetBackgroundOperationStatus(BackgroundOperationStatusEvent evt)
        {
            BackgroundOperation = new ThreadRunBackgroundOperationDto(
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
                task = new ThreadRunBackgroundTaskBuilder(taskId, name);
                _tasks[taskId] = task;
            }

            task.Status = status;
            task.ErrorType = errorType;
            task.ErrorMessage = errorMessage;
            task.SetTimestamp(status, timestamp);
        }

        public ThreadRunDto ToDto() =>
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
                _tasks.Values.Select(task => task.ToDto()).ToList());
    }

    private sealed class ThreadRunBackgroundTaskBuilder
    {
        public ThreadRunBackgroundTaskBuilder(string taskId, string name)
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

        public ThreadRunBackgroundTaskDto ToDto() =>
            new(TaskId, Name, Status, StartedAt, CompletedAt, CancelledAt, FaultedAt, ErrorType, ErrorMessage);
    }
}

internal static class ThreadRunStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}
