using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentBranchService
{
    Task<AgentServiceResult<IReadOnlyList<BranchDto>>> ListBranchesAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<BranchDto>> GetBranchAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<BranchDto>> CreateBranchAsync(
        string agentId,
        string sessionId,
        CreateBranchRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<BranchDto>> ForkBranchAsync(
        string agentId,
        string sessionId,
        string branchId,
        ForkBranchRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<BranchDto>> UpdateBranchAsync(
        string sessionId,
        string branchId,
        UpdateBranchRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> DeleteBranchAsync(
        string sessionId,
        string branchId,
        bool recursive = false,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<IReadOnlyList<MessageDto>>> GetMessagesAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<IReadOnlyList<BranchDto>>> GetSiblingsAsync(
        string sessionId,
        string branchId,
        CancellationToken cancellationToken = default);
}
