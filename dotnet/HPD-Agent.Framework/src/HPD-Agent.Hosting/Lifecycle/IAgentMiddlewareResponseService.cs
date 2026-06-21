using HPD.Events;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentMiddlewareResponseService
{
    Task<AgentServiceResult<RespondResult>> RespondAsync(
        string agentId,
        string sessionId,
        string threadId,
        IResponseEvent response,
        CancellationToken cancellationToken = default);
}
