namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Data transfer object for Session metadata.
/// Transport-agnostic representation used by all hosting platforms.
/// </summary>
/// <param name="Id">Unique identifier for this session</param>
/// <param name="CreatedAt">When this session was created</param>
/// <param name="LastActivity">Last time any branch in this session was updated</param>
/// <param name="Metadata">Session-level metadata (optional)</param>
public record SessionDto(
    string Id,
    DateTime CreatedAt,
    DateTime LastActivity,
    Dictionary<string, object>? Metadata);
