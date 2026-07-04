using HPD.Events;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentMiddlewareResponseService
{
    Task<AgentServiceResult<RespondResult>> AnswerRequestAsync(
        string agentId,
        string sessionId,
        string threadId,
        IResponseEvent response,
        CancellationToken cancellationToken = default);
}
