namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to create a new session.
/// </summary>
/// <param name="AgentId">Optional agent selected by default for the new main thread.</param>
/// <param name="SessionId">Optional custom session ID (auto-generated if not provided)</param>
/// <param name="Metadata">Optional session metadata</param>
public record CreateSessionRequest(
    string? AgentId = null,
    string? SessionId = null,
    Dictionary<string, object>? Metadata = null);
