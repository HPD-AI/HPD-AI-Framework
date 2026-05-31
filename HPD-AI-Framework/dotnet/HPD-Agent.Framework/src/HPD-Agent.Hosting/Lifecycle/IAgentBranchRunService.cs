using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentBranchRunService
{
    Task<AgentServiceResult<IReadOnlyList<BranchRunDto>>> ListRunsAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<BranchRunDto?>> GetActiveRunAsync(
        string agentId,
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<BranchRunDto>> GetRunAsync(
        string agentId,
        string sessionId,
        string branchId,
        string runtimeRunId,
        CancellationToken cancellationToken = default);
}
