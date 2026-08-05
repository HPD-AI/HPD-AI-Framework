namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentMiddlewareResponseService
{
    Task<AgentServiceResult<AgentRespondResult>> AnswerRequestAsync(
        string agentId,
        string sessionId,
        string threadId,
        IAgentResponseEvent response,
        CancellationToken cancellationToken = default);
}
