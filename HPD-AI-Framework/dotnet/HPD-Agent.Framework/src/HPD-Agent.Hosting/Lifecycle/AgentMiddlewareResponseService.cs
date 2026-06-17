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

    public async Task<AgentServiceResult<RespondResult>> RespondAsync(
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

        var result = await agent.RespondIfPendingAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return result.Accepted
            ? AgentServiceResult<RespondResult>.Success(result)
            : AgentServiceResult<RespondResult>.ConflictWith(
                result.Status.ToString(),
                result.Message ?? "Response was not accepted.")
                with { Value = result };
    }

    private async Task<bool> RouteScopeExistsAsync(
        string sessionId,
        string threadId,
        CancellationToken cancellationToken)
    {
        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken);
        if (session == null)
            return false;

        var thread = await _sessionManager.Store.LoadThreadAsync(sessionId, threadId, cancellationToken);
        return thread != null;
    }
}
