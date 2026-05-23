using HPD.Agent.ClientTools;
using HPD.Agent.Middleware;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentMiddlewareResponseService
{
    Task<AgentServiceResult> RespondToPermissionAsync(
        string agentId,
        string sessionId,
        string branchId,
        PermissionResponseEvent response,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> RespondToContinuationAsync(
        string agentId,
        string sessionId,
        string branchId,
        ContinuationResponseEvent response,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> RespondToClarificationAsync(
        string agentId,
        string sessionId,
        string branchId,
        ClarificationResponseEvent response,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> RespondToClientToolAsync(
        string agentId,
        string sessionId,
        string branchId,
        ClientToolInvokeResponseEvent response,
        CancellationToken cancellationToken = default);
}
