using HPD.Agent.Hosting.Data;
using HPD.Agent.Validation;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentDefinitionService : IAgentDefinitionService
{
    private readonly AgentManager _agentManager;

    public AgentDefinitionService(AgentManager agentManager)
    {
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
    }

    public async Task<AgentServiceResult<StoredAgentDto>> CreateAgentAsync(
        CreateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = AgentConfigValidator.Validate(request.Config);
        if (errors.Count > 0)
            return AgentServiceResult<StoredAgentDto>.Validation("config", errors);

        var stored = await _agentManager.CreateDefinitionAsync(
            request.Config,
            request.Name,
            request.Metadata,
            cancellationToken);

        return AgentServiceResult<StoredAgentDto>.Success(ToDto(stored));
    }

    public async Task<IReadOnlyList<AgentSummaryDto>> ListAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        var agents = await _agentManager.ListDefinitionsAsync(cancellationToken);
        return agents.Select(ToSummaryDto).ToList();
    }

    public async Task<StoredAgentDto?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var stored = await _agentManager.GetDefinitionAsync(agentId, cancellationToken);
        return stored == null ? null : ToDto(stored);
    }

    public async Task<AgentServiceResult<StoredAgentDto>> UpdateAgentAsync(
        string agentId,
        UpdateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(request);

        var errors = AgentConfigValidator.Validate(request.Config);
        if (errors.Count > 0)
            return AgentServiceResult<StoredAgentDto>.Validation("config", errors);

        var existing = await _agentManager.GetDefinitionAsync(agentId, cancellationToken);
        if (existing == null)
            return AgentServiceResult<StoredAgentDto>.NotFound;

        var stored = await _agentManager.UpdateDefinitionAsync(agentId, request.Config, cancellationToken);
        return AgentServiceResult<StoredAgentDto>.Success(ToDto(stored));
    }

    public async Task<AgentServiceResult> DeleteAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var existing = await _agentManager.GetDefinitionAsync(agentId, cancellationToken);
        if (existing == null)
            return AgentServiceResult.NotFound;

        await _agentManager.DeleteDefinitionAsync(agentId, cancellationToken);
        return AgentServiceResult.Success;
    }

    private static StoredAgentDto ToDto(StoredAgent stored) => new(
        stored.Id,
        stored.Name,
        stored.Config,
        stored.CreatedAt,
        stored.UpdatedAt,
        stored.Metadata);

    private static AgentSummaryDto ToSummaryDto(StoredAgent stored) => new(
        stored.Id,
        stored.Name,
        stored.CreatedAt,
        stored.UpdatedAt,
        stored.Metadata);
}
