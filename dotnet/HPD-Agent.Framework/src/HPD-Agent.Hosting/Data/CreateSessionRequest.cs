namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to create a new session.
/// </summary>
/// <param name="AgentId">Optional stable owner override for the default main thread.</param>
/// <param name="SessionId">Optional custom session ID (auto-generated if not provided)</param>
/// <param name="Metadata">Optional session metadata</param>
public record CreateSessionRequest(
    string? AgentId = null,
    string? SessionId = null,
    Dictionary<string, object>? Metadata = null);
