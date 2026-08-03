namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentMiddlewareResponseService : IAgentMiddlewareResponseService
{
    private readonly SessionManager _sessionManager;
    private readonly AgentManager _agentManager;

    public AgentMiddlewareResponseService(SessionManager sessionManager, AgentManager agentManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
    }

    public async Task<AgentServiceResult<AgentRespondResult>> AnswerRequestAsync(
        string agentId,
        string sessionId,
        string threadId,
        IAgentResponseEvent response,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteScopeExistsAsync(sessionId, threadId, cancellationToken))
            return AgentServiceResult<AgentRespondResult>.NotFound;

        var agent = _agentManager.GetRuntimeAgent(agentId, sessionId, threadId);
        if (agent == null)
        {
            return AgentServiceResult<AgentRespondResult>.Success(
                await ClassifyUnavailableResponseAsync(
                    sessionId, threadId, response.RequestId, cancellationToken).ConfigureAwait(false));
        }

        if (response is not AgentEvent agentResponse)
            throw new ArgumentException("Hosted request responses must be AgentEvents.", nameof(response));

        var scopedResponse = (IAgentResponseEvent)(agentResponse with
        {
            SessionId = string.IsNullOrWhiteSpace(agentResponse.SessionId) ? sessionId : agentResponse.SessionId,
            ThreadId = string.IsNullOrWhiteSpace(agentResponse.ThreadId) ? threadId : agentResponse.ThreadId
        });
        var result = await agent.TryAnswerRequestAsync(scopedResponse, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == AgentRespondStatus.NotFound)
        {
            return AgentServiceResult<AgentRespondResult>.Success(
                await ClassifyUnavailableResponseAsync(
                    sessionId, threadId, response.RequestId, cancellationToken).ConfigureAwait(false));
        }

        return AgentServiceResult<AgentRespondResult>.Success(result);
    }

    private async Task<AgentRespondResult> ClassifyUnavailableResponseAsync(
        string sessionId,
        string threadId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var journal = await _sessionManager.Store.CollectThreadEventsAsync(
            new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false) ?? [];
        var activeExecutionId = _sessionManager
            .GetActiveThreadExecution(sessionId, threadId)?
            .ThreadExecutionId;
        return AgentRequestProjector.ClassifyResponseAttempt(
            journal,
            requestId,
            activeExecutionId);
    }

    private async Task<bool> RouteScopeExistsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken);
        if (session == null)
            return false;

        return await _sessionManager.Store.GetThreadAsync(
            new ThreadKey(sessionId, threadId), cancellationToken).ConfigureAwait(false) != null;
    }
}
