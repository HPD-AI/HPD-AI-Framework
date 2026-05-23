using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;
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

    public Task<AgentServiceResult> RespondToPermissionAsync(
        string agentId,
        string sessionId,
        string branchId,
        PermissionResponseEvent response,
        CancellationToken cancellationToken = default) =>
        RespondAsync(agentId, sessionId, branchId, response, cancellationToken);

    public Task<AgentServiceResult> RespondToContinuationAsync(
        string agentId,
        string sessionId,
        string branchId,
        ContinuationResponseEvent response,
        CancellationToken cancellationToken = default) =>
        RespondAsync(agentId, sessionId, branchId, response, cancellationToken);

    public Task<AgentServiceResult> RespondToClarificationAsync(
        string agentId,
        string sessionId,
        string branchId,
        ClarificationResponseEvent response,
        CancellationToken cancellationToken = default) =>
        RespondAsync(agentId, sessionId, branchId, response, cancellationToken);

    public Task<AgentServiceResult> RespondToClientToolAsync(
        string agentId,
        string sessionId,
        string branchId,
        ClientToolInvokeResponseEvent response,
        CancellationToken cancellationToken = default) =>
        RespondAsync(agentId, sessionId, branchId, response, cancellationToken);

    private async Task<AgentServiceResult> RespondAsync(
        string agentId,
        string sessionId,
        string branchId,
        IBidirectionalEvent response,
        CancellationToken cancellationToken)
    {
        if (!await RouteScopeExistsAsync(sessionId, branchId, cancellationToken))
            return AgentServiceResult.NotFound;

        var agent = _agentManager.GetAgent(agentId);
        if (agent == null)
            return AgentServiceResult.NotFound;

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
