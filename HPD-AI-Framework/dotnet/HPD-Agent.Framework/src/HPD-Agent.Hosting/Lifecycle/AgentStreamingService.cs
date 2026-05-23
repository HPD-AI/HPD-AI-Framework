using HPD.Agent;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentStreamingService : IAgentStreamingService
{
    private readonly SessionManager _sessionManager;
    private readonly AgentManager _agentManager;

    public AgentStreamingService(SessionManager sessionManager, AgentManager agentManager)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
    }

    public async Task<AgentServiceResult<AgentStreamLease>> BeginStreamAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default)
    {
        if (await _sessionManager.Store.LoadSessionAsync(sessionId, cancellationToken) == null)
            return AgentServiceResult<AgentStreamLease>.NotFound;

        if (await _sessionManager.Store.LoadBranchAsync(sessionId, branchId, cancellationToken) == null)
            return AgentServiceResult<AgentStreamLease>.NotFound;

        if (!_sessionManager.TryAcquireStreamLock(sessionId, branchId))
            return AgentServiceResult<AgentStreamLease>.Conflict;

        try
        {
            var agent = await _agentManager.GetOrBuildAgentAsync(agentId, cancellationToken);
            return AgentServiceResult<AgentStreamLease>.Success(new AgentStreamLease(agent));
        }
        catch
        {
            _sessionManager.ReleaseStreamLock(sessionId, branchId);
            throw;
        }
    }

    public void ReleaseStream(string sessionId, string branchId)
    {
        _sessionManager.ReleaseStreamLock(sessionId, branchId);
    }

    public AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string branchId)
    {
        return input switch
        {
            UserTextInputEvent text => text with
            {
                AgentId = agentId,
                SessionId = sessionId,
                BranchId = branchId
            },
            UserMessagesInputEvent messages => messages with
            {
                AgentId = agentId,
                SessionId = sessionId,
                BranchId = branchId
            },
            _ => input
        };
    }
}
