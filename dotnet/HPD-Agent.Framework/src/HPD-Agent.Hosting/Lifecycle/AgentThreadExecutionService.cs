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
            execution.Operations.Select(operation => new ThreadExecutionOperationDto(
                operation.OperationId,
                operation.ProviderOperationId,
                operation.Name,
                operation.SourceKind.ToString().ToLowerInvariant(),
                operation.ProviderStatus.ToString().ToLowerInvariant(),
                operation.ObservationStatus.ToString().ToLowerInvariant(),
                operation.Control.Kind.ToString().ToLowerInvariant(),
                operation.Control.Capabilities.ToString().ToLowerInvariant(),
                operation.Control.HandleId,
                operation.Version,
                operation.RegisteredAt,
                operation.StartedAt,
                operation.UpdatedAt,
                operation.FinishedAt,
                operation.Completion?.Summary,
                operation.Completion?.ArtifactReferences,
                operation.Failure?.Code,
                operation.Failure?.Message,
                operation.Metadata)).ToList());
}
