namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to search/filter sessions.
/// Uses POST (instead of GET) to allow complex filter body.
/// </summary>
/// <param name="Metadata">Filter by metadata fields (all must match)</param>
/// <param name="Offset">Number of sessions to skip (for pagination)</param>
/// <param name="Limit">Maximum number of sessions to return</param>
public record SearchSessionsRequest(
    Dictionary<string, object>? Metadata,
    int Offset = 0,
    int Limit = 50);
