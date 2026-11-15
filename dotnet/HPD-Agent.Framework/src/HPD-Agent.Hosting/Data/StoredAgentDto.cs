using System.Text.Json.Serialization;

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

/// <summary>Request body for explicitly creating a new agent definition.</summary>
public record CreateAgentRequest(
    string AgentId,
    AgentConfig Config,
    Dictionary<string, object>? Metadata = null);

/// <summary>Patch request for an existing agent definition.</summary>
/// <remarks>
/// Omitted sections remain unchanged. Supplying <c>clearMetadata: true</c> clears
/// metadata, while an empty object replaces metadata with an empty object.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateAgentRequest
{
    [JsonConstructor]
    public UpdateAgentRequest()
    {
    }

    /// <summary>Creates a request that replaces every portable configuration field.</summary>
    public UpdateAgentRequest(AgentConfigUpdate config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentConfigUpdate Config { get; init; } = null!;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object> Metadata { get; init; } = null!;

    /// <summary>Clears metadata in the reset form used by typed C# callers.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ClearMetadata { get; init; }
}
