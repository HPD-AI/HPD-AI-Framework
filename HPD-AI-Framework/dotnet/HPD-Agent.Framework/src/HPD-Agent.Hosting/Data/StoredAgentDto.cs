namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Lightweight summary of a stored agent definition — returned by GET /agents list.
/// Does not include the config body; fetch GET /agents/{id} for the full definition.
/// </summary>
public record AgentSummaryDto(
    string Id,
    string Name,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Dictionary<string, object>? Metadata);

/// <summary>
/// Data transfer object for a stored agent definition.
/// </summary>
public record StoredAgentDto(
    string Id,
    string Name,
    AgentConfig Config,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Dictionary<string, object>? Metadata);

/// <summary>Request body for creating a new agent definition.</summary>
public record CreateAgentRequest(
    string Name,
    AgentConfig Config,
    Dictionary<string, object>? Metadata = null);

/// <summary>Request body for updating an existing agent definition.</summary>
public record UpdateAgentRequest(AgentConfig Config);
