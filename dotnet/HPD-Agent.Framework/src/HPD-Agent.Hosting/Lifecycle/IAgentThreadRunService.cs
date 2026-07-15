using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentThreadRunService
{
    Task<AgentServiceResult<IReadOnlyList<ThreadRunDto>>> ListRunsAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadRunDto>> GetRunAsync(
        string agentId,
        string sessionId,
        string threadId,
        string runtimeRunId,
        CancellationToken cancellationToken = default);
}
