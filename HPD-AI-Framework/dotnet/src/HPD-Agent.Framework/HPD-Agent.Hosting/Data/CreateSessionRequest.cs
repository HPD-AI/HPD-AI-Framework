namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to create a new session.
/// </summary>
/// <param name="SessionId">Optional custom session ID (auto-generated if not provided)</param>
/// <param name="Metadata">Optional session metadata</param>
public record CreateSessionRequest(
    string? SessionId,
    Dictionary<string, object>? Metadata);
