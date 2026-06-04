using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentBranchRunService : IAgentBranchRunService
{
    private readonly SessionManager _sessionManager;

    public AgentBranchRunService(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<AgentServiceResult<IReadOnlyList<BranchRunDto>>> ListRunsAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var runs = await LoadProjectedRunsAsync(agentId, sessionId, branchId, cancellationToken)
            .ConfigureAwait(false);

        return runs == null
            ? AgentServiceResult<IReadOnlyList<BranchRunDto>>.NotFound
            : AgentServiceResult<IReadOnlyList<BranchRunDto>>.Success(runs);
    }

    public async Task<AgentServiceResult<BranchRunDto?>> GetActiveRunAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        var runs = await LoadProjectedRunsAsync(agentId, sessionId, branchId, cancellationToken)
            .ConfigureAwait(false);
        if (runs == null)
            return AgentServiceResult<BranchRunDto?>.NotFound;

        return AgentServiceResult<BranchRunDto?>.Success(
            runs.LastOrDefault(run => run.Status == BranchRunStatuses.Active));
    }

    public async Task<AgentServiceResult<BranchRunDto>> GetRunAsync(
        string agentId,
        string sessionId,
        string branchId,
        string runtimeRunId,
        CancellationToken cancellationToken = default)
    {
        var runs = await LoadProjectedRunsAsync(agentId, sessionId, branchId, cancellationToken)
            .ConfigureAwait(false);
        if (runs == null)
            return AgentServiceResult<BranchRunDto>.NotFound;

        var run = runs.FirstOrDefault(candidate => candidate.RuntimeRunId == runtimeRunId);
        return run == null
            ? AgentServiceResult<BranchRunDto>.NotFound
            : AgentServiceResult<BranchRunDto>.Success(run);
    }

    private async Task<IReadOnlyList<BranchRunDto>?> LoadProjectedRunsAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken)
    {
        var document = await _sessionManager.Repository.LoadBranchDocumentAsync(sessionId, branchId, cancellationToken)
            .ConfigureAwait(false);
        if (document == null && await _sessionManager.Repository.LoadBranchAsync(sessionId, branchId, cancellationToken)
                .ConfigureAwait(false) == null)
            return null;

        var events = document?.Events.OrderBy(evt => evt.SequenceNumber).ToList() ?? [];
        var runs = ProjectRuns(agentId, sessionId, branchId, events);
        MergeActiveRun(agentId, sessionId, branchId, runs);

        return runs.Select(run => run.ToDto()).ToList();
    }

    private void MergeActiveRun(
        string agentId,
        string sessionId,
        string branchId,
        List<BranchRunBuilder> runs)
    {
        var active = _sessionManager.GetActiveBranchRun(sessionId, branchId);
        if (active == null || active.AgentId != agentId)
            return;

        if (runs.Any(run => run.RuntimeRunId == active.RuntimeRunId))
            return;

        runs.Add(new BranchRunBuilder(
            active.RuntimeRunId,
            active.AgentId,
            active.SessionId,
            active.BranchId,
            active.StartedAt));
    }

    private static List<BranchRunBuilder> ProjectRuns(
        string agentId,
        string sessionId,
        string branchId,
        IReadOnlyList<AgentEvent> events)
    {
        var runs = new List<BranchRunBuilder>();
        BranchRunBuilder? current = null;

        foreach (var evt in events)
        {
            switch (evt)
            {
                case BranchRunStartedEvent started when started.AgentId == agentId:
                    current = new BranchRunBuilder(
                        started.RuntimeRunId,
                        started.AgentId,
                        sessionId,
                        branchId,
                        started.StartedAt);
                    runs.Add(current);
                    break;

                case BranchRunCompletedEvent completed when completed.AgentId == agentId:
                    current = runs.LastOrDefault(run => run.RuntimeRunId == completed.RuntimeRunId);
                    if (current != null)
                        current.Complete(completed);
                    break;

                case BackgroundOperationStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == BranchRunStatuses.Active);
                    current?.SetBackgroundOperation(started);
                    break;

                case BackgroundOperationStatusEvent status:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == BranchRunStatuses.Active);
                    current?.SetBackgroundOperationStatus(status);
                    break;

                case ToolCallBackgroundTaskStartedEvent started:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == BranchRunStatuses.Active);
                    current?.SetTask(started.TaskId, started.Name, "started", started.StartedAt);
                    break;

                case ToolCallBackgroundTaskCompletedEvent completed:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == BranchRunStatuses.Active);
                    current?.SetTask(completed.TaskId, completed.Name, "completed", completed.CompletedAt);
                    break;

                case ToolCallBackgroundTaskCancelledEvent cancelled:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == BranchRunStatuses.Active);
                    current?.SetTask(cancelled.TaskId, cancelled.Name, "cancelled", cancelled.CancelledAt);
                    break;

                case ToolCallBackgroundTaskFaultedEvent faulted:
                    current ??= runs.LastOrDefault(run => run.AgentId == agentId && run.Status == BranchRunStatuses.Active);
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

    private sealed class BranchRunBuilder
    {
        private readonly Dictionary<string, BranchRunBackgroundTaskBuilder> _tasks = new();

        public BranchRunBuilder(
            string runtimeRunId,
            string agentId,
            string sessionId,
            string branchId,
            DateTimeOffset startedAt)
        {
            RuntimeRunId = runtimeRunId;
            AgentId = agentId;
            SessionId = sessionId;
            BranchId = branchId;
            StartedAt = startedAt;
        }

        public string RuntimeRunId { get; }
        public string AgentId { get; }
        public string SessionId { get; }
        public string BranchId { get; }
        public DateTimeOffset StartedAt { get; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public string Status { get; private set; } = BranchRunStatuses.Active;
        public BranchRunErrorDto? Error { get; private set; }
        public BranchRunBackgroundOperationDto? BackgroundOperation { get; private set; }

        public void Complete(BranchRunCompletedEvent completed)
        {
            CompletedAt = completed.Timestamp;
            Status = completed.ErrorType != null
                ? BranchRunStatuses.Failed
                : completed.Cancelled ? BranchRunStatuses.Cancelled : BranchRunStatuses.Completed;
            Error = completed.ErrorType == null && completed.ErrorMessage == null
                ? null
                : new BranchRunErrorDto(completed.ErrorType, completed.ErrorMessage);
        }

        public void SetBackgroundOperation(BackgroundOperationStartedEvent evt)
        {
            BackgroundOperation = new BranchRunBackgroundOperationDto(
                evt.Status.Value,
                evt.OperationId,
                null,
                SerializeToken(evt.ContinuationToken));
        }

        public void SetBackgroundOperationStatus(BackgroundOperationStatusEvent evt)
        {
            BackgroundOperation = new BranchRunBackgroundOperationDto(
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
                task = new BranchRunBackgroundTaskBuilder(taskId, name);
                _tasks[taskId] = task;
            }

            task.Status = status;
            task.ErrorType = errorType;
            task.ErrorMessage = errorMessage;
            task.SetTimestamp(status, timestamp);
        }

        public BranchRunDto ToDto() =>
            new(
                RuntimeRunId,
                AgentId,
                SessionId,
                BranchId,
                Status,
                StartedAt,
                CompletedAt,
                Error,
                BackgroundOperation,
                _tasks.Values.Select(task => task.ToDto()).ToList());
    }

    private sealed class BranchRunBackgroundTaskBuilder
    {
        public BranchRunBackgroundTaskBuilder(string taskId, string name)
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

        public BranchRunBackgroundTaskDto ToDto() =>
            new(TaskId, Name, Status, StartedAt, CompletedAt, CancelledAt, FaultedAt, ErrorType, ErrorMessage);
    }
}

internal static class BranchRunStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Failed = "failed";
}
