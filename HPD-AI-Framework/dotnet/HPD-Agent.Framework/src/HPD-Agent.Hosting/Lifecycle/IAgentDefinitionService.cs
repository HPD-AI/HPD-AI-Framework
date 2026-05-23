using HPD.Agent.Hosting.Data;

namespace HPD.Agent.Hosting.Lifecycle;

public interface IAgentDefinitionService
{
    Task<AgentServiceResult<StoredAgentDto>> CreateAgentAsync(
        CreateAgentRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentSummaryDto>> ListAgentsAsync(
        CancellationToken cancellationToken = default);

    Task<StoredAgentDto?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult<StoredAgentDto>> UpdateAgentAsync(
        string agentId,
        UpdateAgentRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentServiceResult> DeleteAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
