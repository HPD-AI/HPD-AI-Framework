using HPD.Events;

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

    public async Task<AgentServiceResult<RespondResult>> AnswerRequestAsync(
        string agentId,
        string sessionId,
        string threadId,
        IResponseEvent response,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteScopeExistsAsync(sessionId, threadId, cancellationToken))
            return AgentServiceResult<RespondResult>.NotFound;

        var agent = _agentManager.GetRuntimeAgent(agentId, sessionId, threadId);
        if (agent == null)
        {
            return AgentServiceResult<RespondResult>.ConflictWith(
                "ThreadRuntimeNotActive",
                $"Thread '{threadId}' in session '{sessionId}' does not have an active runtime for agent '{agentId}'.");
        }

        if (response is not AgentEvent agentResponse)
            throw new ArgumentException("Hosted request responses must be AgentEvents.", nameof(response));

        var scopedResponse = (IResponseEvent)(agentResponse with
        {
            SessionId = string.IsNullOrWhiteSpace(agentResponse.SessionId) ? sessionId : agentResponse.SessionId,
            ThreadId = string.IsNullOrWhiteSpace(agentResponse.ThreadId) ? threadId : agentResponse.ThreadId
        });
        var result = await agent.TryAnswerRequestAsync(scopedResponse, cancellationToken)
            .ConfigureAwait(false);

        return result.Accepted
            ? AgentServiceResult<RespondResult>.Success(result)
            : AgentServiceResult<RespondResult>.Conflict with { Value = result };
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
