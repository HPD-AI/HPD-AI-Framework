using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.Studio;

internal sealed class DefaultAgentStudioConfigurationAuthority(IAgentDefinitionService agents)
    : IAgentStudioConfigurationAuthority
{
    public async ValueTask<AgentStudioConfigurationSnapshot?> GetAsync(
        string agentId, CancellationToken cancellationToken)
    {
        StoredAgentDto? value = await agents.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        return value is null ? null : new(value.Id, value.Config, value.UpdatedAt);
    }

    public async ValueTask<AgentStudioConfigurationWriteResult> WriteAsync(
        string agentId, AgentConfig draft, CancellationToken cancellationToken)
    {
        AgentServiceResult<StoredAgentDto> result = await agents.UpdateAgentAsync(agentId,
            new UpdateAgentRequest(AgentConfigUpdate.From(draft)), cancellationToken).ConfigureAwait(false);
        return result.Status switch
        {
            AgentServiceStatus.Success when result.Value is not null =>
                AgentStudioConfigurationWriteResult.Success(result.Value.UpdatedAt),
            AgentServiceStatus.NotFound => AgentStudioConfigurationWriteResult.NotFound,
            AgentServiceStatus.Conflict => AgentStudioConfigurationWriteResult.Conflict,
            AgentServiceStatus.ValidationError => AgentStudioConfigurationWriteResult.Validation,
            _ => AgentStudioConfigurationWriteResult.Conflict,
        };
    }
}
