using HPD.Agent;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentStreamingService
{
    Task<AgentServiceResult<AgentStreamLease>> BeginStreamAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    void ReleaseStream(string sessionId, string branchId);

    AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string branchId);
}

public sealed record AgentStreamLease(Agent Agent);
