using HPD.Agent;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentStreamingService
{
    Task<AgentServiceResult<AgentStreamLease>> GetAgentForBranchAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> SubmitInputAsync(
        string agentId,
        string sessionId,
        string branchId,
        AgentInputEvent input,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> InterruptAsync(
        string agentId,
        string sessionId,
        string branchId,
        InterruptionRequestEvent interruption,
        CancellationToken cancellationToken = default);

    AgentInputEvent ApplyRouteScope(
        AgentInputEvent input,
        string agentId,
        string sessionId,
        string branchId,
        string? runtimeRunId = null);
}

public sealed record AgentStreamLease(Agent Agent);
