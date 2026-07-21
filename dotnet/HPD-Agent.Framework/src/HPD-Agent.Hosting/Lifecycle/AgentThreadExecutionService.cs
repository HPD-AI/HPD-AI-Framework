using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentThreadExecutionService : IAgentThreadExecutionService
{
    private readonly SessionManager _sessionManager;

    public AgentThreadExecutionService(SessionManager sessionManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public async Task<AgentServiceResult<IReadOnlyList<ThreadExecutionDto>>> ListExecutionsAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var executions = await LoadProjectedExecutionsAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);

        return executions == null
            ? AgentServiceResult<IReadOnlyList<ThreadExecutionDto>>.NotFound
            : AgentServiceResult<IReadOnlyList<ThreadExecutionDto>>.Success(executions);
    }

    public async Task<AgentServiceResult<ThreadExecutionDto>> GetExecutionAsync(
        string agentId,
        string sessionId,
        string threadId,
        string threadExecutionId,
        CancellationToken cancellationToken = default)
    {
        var executions = await LoadProjectedExecutionsAsync(agentId, sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (executions == null)
            return AgentServiceResult<ThreadExecutionDto>.NotFound;

        var execution = executions.FirstOrDefault(candidate => candidate.ThreadExecutionId == threadExecutionId);
        return execution == null
            ? AgentServiceResult<ThreadExecutionDto>.NotFound
            : AgentServiceResult<ThreadExecutionDto>.Success(execution);
    }

    private async Task<IReadOnlyList<ThreadExecutionDto>?> LoadProjectedExecutionsAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        var events = await _sessionManager.GetThreadExecutionProjectionEventsAsync(sessionId, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (events is null)
            return null;

        var active = _sessionManager.GetActiveThreadExecution(sessionId, threadId);
        var activeThreadExecutionId = active?.AgentId == agentId
            ? active.ThreadExecutionId
            : null;

        var executions = ThreadExecutionProjector.Project(
                agentId,
                sessionId,
                threadId,
                events,
                activeThreadExecutionId)
            .ToList();

        if (active is not null &&
            active.AgentId == agentId &&
            executions.All(execution => execution.ThreadExecutionId != active.ThreadExecutionId))
        {
            executions.Add(new ThreadExecutionProjection(
                active.ThreadExecutionId,
                active.AgentId,
                sessionId,
                threadId,
                ThreadExecutionStatus.Active,
                active.StartedAt,
                null,
                null,
                null,
                [],
                []));
        }

        return executions.Select(ToDto).ToList();
    }

    private static ThreadExecutionDto ToDto(ThreadExecutionProjection execution) =>
        new(
            execution.ThreadExecutionId,
            execution.AgentId,
            execution.SessionId,
            execution.ThreadId,
            execution.Status,
            execution.StartedAt,
            execution.FinishedAt,
            execution.Error == null ? null : new ThreadExecutionErrorDto(execution.Error.Type, execution.Error.Message),
            execution.ModelBackgroundOperation == null
                ? null
                : new ThreadExecutionModelBackgroundOperationDto(
                    execution.ModelBackgroundOperation.Status,
                    execution.ModelBackgroundOperation.OperationId,
                    execution.ModelBackgroundOperation.StatusMessage,
                    execution.ModelBackgroundOperation.ContinuationToken),
            execution.BackgroundTasks.Select(task => new ThreadExecutionBackgroundTaskDto(
                task.TaskId,
                task.Name,
                task.SourceKind,
                task.SourceId,
                new ThreadExecutionBackgroundTaskNotificationDto(
                    task.Notification.Kind,
                    task.Notification.StrategyName),
                task.Status,
                task.StartedAt,
                task.CompletedAt,
                task.CancelledAt,
                task.FaultedAt,
                task.ErrorType,
                task.ErrorMessage)).ToList(),
            execution.BackgroundHandles.Select(handle => new ThreadExecutionBackgroundHandleDto(
                handle.HandleId,
                handle.Name,
                handle.HandleKind,
                handle.SourceKind,
                handle.SourceId,
                handle.Status,
                handle.SupportedOperations.ToString(),
                handle.RegisteredAt,
                handle.UpdatedAt,
                handle.Metadata)).ToList());
}
