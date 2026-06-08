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

    public async Task<AgentServiceResult> RespondAsync(
        string agentId,
        string sessionId,
        string branchId,
        IBidirectionalEvent response,
        CancellationToken cancellationToken = default)
    {
        if (!await RouteScopeExistsAsync(sessionId, branchId, cancellationToken))
            return AgentServiceResult.NotFound;

        var agent = _agentManager.GetRuntimeAgent(agentId, sessionId, branchId);
        if (agent == null)
        {
            return AgentServiceResult.ConflictWith(
                "BranchRuntimeNotActive",
                $"Branch '{branchId}' in session '{sessionId}' does not have an active runtime for agent '{agentId}'.");
        }

        return await agent.TryRespondAsync(response, cancellationToken)
            ? AgentServiceResult.Success
            : AgentServiceResult.Conflict;
    }

    private async Task<bool> RouteScopeExistsAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken)
    {
        var session = await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken);
        if (session == null)
            return false;

        var branch = await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken);
        return branch != null;
    }
}
