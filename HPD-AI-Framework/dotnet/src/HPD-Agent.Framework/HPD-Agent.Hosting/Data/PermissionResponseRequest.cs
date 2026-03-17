namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to respond to a permission prompt from the agent.
/// </summary>
/// <param name="SessionId">The session identifier (required for MAUI, optional for ASP.NET Core where it comes from route)</param>
/// <param name="PermissionId">The permission request identifier</param>
/// <param name="Approved">Whether the permission was approved</param>
/// <param name="Reason">Optional reason for denial</param>
/// <param name="Choice">Optional choice identifier (for multi-choice permissions)</param>
/// <param name="AgentId">Agent definition ID. Defaults to "default" if not specified.</param>
public record PermissionResponseRequest(
    string? SessionId,
    string PermissionId,
    bool Approved,
    string? Reason,
    string? Choice,
    string? AgentId = null);
