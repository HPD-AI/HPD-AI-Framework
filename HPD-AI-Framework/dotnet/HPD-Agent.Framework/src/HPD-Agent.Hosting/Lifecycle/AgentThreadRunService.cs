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
            runs.LastOrDefault(run => run.Status == ThreadRunStatus.Active));
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

        var active = _sessionManager.GetActiveThreadRun(sessionId, threadId);
        var activeRuntimeRunId = active?.AgentId == agentId
            ? active.RuntimeRunId
            : null;

        var runs = ThreadRunProjector.Project(
                agentId,
                sessionId,
                threadId,
                document?.Events ?? [],
                activeRuntimeRunId)
            .ToList();

        return runs.Select(ToDto).ToList();
    }

    private static ThreadRunDto ToDto(ThreadRunProjection run) =>
        new(
            run.RuntimeRunId,
            run.AgentId,
            run.SessionId,
            run.ThreadId,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.Error == null ? null : new ThreadRunErrorDto(run.Error.Type, run.Error.Message),
            run.BackgroundOperation == null
                ? null
                : new ThreadRunBackgroundOperationDto(
                    run.BackgroundOperation.Status,
                    run.BackgroundOperation.OperationId,
                    run.BackgroundOperation.StatusMessage,
                    run.BackgroundOperation.ContinuationToken),
            run.BackgroundTasks.Select(task => new ThreadRunBackgroundTaskDto(
                task.TaskId,
                task.Name,
                task.Status,
                task.StartedAt,
                task.CompletedAt,
                task.CancelledAt,
                task.FaultedAt,
                task.ErrorType,
                task.ErrorMessage)).ToList());
}
