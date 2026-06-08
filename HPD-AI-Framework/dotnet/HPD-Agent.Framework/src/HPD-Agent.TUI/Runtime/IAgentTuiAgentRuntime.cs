namespace HPD.Agent.TUI.Runtime;

public interface IAgentTuiAgentDefinitionRuntime
{
    bool CanSwitchAgents { get; }

    Task<IReadOnlyList<AgentTuiAgentInfo>> ListAgentsAsync(
        CancellationToken cancellationToken = default);

    Task<AgentTuiAgentInfo?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<AgentTuiAgentInfo> CreateAgentAsync(
        AgentTuiCreateAgentRequest request,
        CancellationToken cancellationToken = default);

    Task<AgentTuiAgentInfo> UpdateAgentAsync(
        string agentId,
        AgentTuiUpdateAgentRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}

public interface IAgentTuiAgentRuntime : IAgentTuiAgentDefinitionRuntime
{
}

public sealed record AgentTuiCreateAgentRequest(
    string Name,
    AgentConfig Config,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record AgentTuiUpdateAgentRequest(
    AgentConfig Config);

public sealed record AgentTuiAgentInfo(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, object?>? Metadata = null,
    AgentConfig? Config = null);
