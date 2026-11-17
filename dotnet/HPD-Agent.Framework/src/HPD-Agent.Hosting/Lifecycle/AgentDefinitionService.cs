using HPD.Agent.Hosting.Data;
using HPD.Agent.Validation;

namespace HPD.Agent.Hosting.Lifecycle;

public sealed class AgentDefinitionService : IAgentDefinitionService
{
    private readonly AgentManager _agentManager;
    private readonly AgentConfigValidationLimits? _validationLimits;

    public AgentDefinitionService(AgentManager agentManager)
        : this(agentManager, validationLimits: null)
    {
    }

    public AgentDefinitionService(
        AgentManager agentManager,
        AgentConfigValidationLimits? validationLimits)
    {
        _agentManager = agentManager ?? throw new ArgumentNullException(nameof(agentManager));
        _validationLimits = validationLimits;
    }

    public async Task<AgentServiceResult<StoredAgentDto>> CreateAgentAsync(
        CreateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentId);

        var errors = AgentConfigValidator.Validate(request.Config, _validationLimits);
        if (errors.Count > 0)
            return AgentServiceResult<StoredAgentDto>.Validation("config", errors);

        if (await _agentManager.GetDefinitionAsync(request.AgentId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return AgentServiceResult<StoredAgentDto>.ConflictWith(
                "AgentAlreadyExists",
                $"Agent definition '{request.AgentId}' already exists.");
        }

        StoredAgent stored;
        try
        {
            stored = await _agentManager.CreateDefinitionAsync(
                request.Config,
                request.Metadata,
                request.AgentId,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            if (await _agentManager.GetDefinitionAsync(request.AgentId, cancellationToken)
                    .ConfigureAwait(false) is null)
                throw;

            return AgentServiceResult<StoredAgentDto>.ConflictWith(
                "AgentAlreadyExists",
                $"Agent definition '{request.AgentId}' already exists.");
        }

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

        if (request.Config is null && request.Metadata is null && request.ClearMetadata is not true)
        {
            return AgentServiceResult<StoredAgentDto>.Validation(
                "request",
                ["At least one of 'config', 'metadata', or 'clearMetadata' must be supplied."]);
        }
        if (request.ClearMetadata is true && request.Metadata is not null)
        {
            return AgentServiceResult<StoredAgentDto>.Validation(
                "metadata",
                ["metadata and clearMetadata cannot be supplied together."]);
        }

        var existing = await _agentManager.GetDefinitionAsync(agentId, cancellationToken);
        if (existing == null)
            return AgentServiceResult<StoredAgentDto>.NotFound;

        var patchErrors = new List<string>();
        var config = request.Config is null
            ? AgentConfigSnapshot.Create(existing.Config)
            : request.Config.ApplyTo(existing.Config, patchErrors);
        var errors = patchErrors
            .Concat(AgentConfigValidator.Validate(config, _validationLimits))
            .ToList();
        if (errors.Count > 0)
            return AgentServiceResult<StoredAgentDto>.Validation("config", errors);

        var metadata = request.ClearMetadata is true
            ? null
            : request.Metadata ?? existing.Metadata;

        try
        {
            var stored = await _agentManager.UpdateDefinitionIfUnchangedAsync(
                agentId,
                existing,
                config,
                metadata,
                replaceMetadata: true,
                cancellationToken);
            return AgentServiceResult<StoredAgentDto>.Success(ToDto(stored));
        }
        catch (AgentStoreConcurrencyException)
        {
            return AgentServiceResult<StoredAgentDto>.ConflictWith(
                "AgentDefinitionConcurrencyConflict",
                $"Agent definition '{agentId}' changed after it was read. Refresh and retry the update.");
        }
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
        stored.Config.Name,
        stored.Config,
        stored.CreatedAt,
        stored.UpdatedAt,
        stored.Metadata);

    private static AgentSummaryDto ToSummaryDto(StoredAgent stored) => new(
        stored.Id,
        stored.Config.Name,
        stored.CreatedAt,
        stored.UpdatedAt,
        stored.Metadata);
}
