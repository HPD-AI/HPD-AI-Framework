using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentThreadExecutionService
{
    Task<AgentServiceResult<IReadOnlyList<ThreadExecutionDto>>> ListExecutionsAsync(
        string agentId,
        string sessionId,
        string threadId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<ThreadExecutionDto>> GetExecutionAsync(
        string agentId,
        string sessionId,
        string threadId,
        string threadExecutionId,
        CancellationToken cancellationToken = default);
}
