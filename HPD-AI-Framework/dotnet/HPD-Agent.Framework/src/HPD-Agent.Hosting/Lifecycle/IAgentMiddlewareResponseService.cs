using HPD.Events;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentMiddlewareResponseService
{
    Task<AgentServiceResult> RespondAsync(
        string agentId,
        string sessionId,
        string branchId,
        IBidirectionalEvent response,
        CancellationToken cancellationToken = default);
}
