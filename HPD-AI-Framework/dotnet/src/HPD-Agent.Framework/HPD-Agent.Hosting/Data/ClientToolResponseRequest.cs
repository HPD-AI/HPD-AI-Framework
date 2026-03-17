namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Request to respond to a client tool execution request from the agent.
/// </summary>
/// <param name="SessionId">The session identifier (required for MAUI, optional for ASP.NET Core where it comes from route)</param>
/// <param name="RequestId">The client tool request identifier</param>
/// <param name="Success">Whether the tool execution succeeded</param>
/// <param name="Content">Tool result content items</param>
/// <param name="ErrorMessage">Error message if execution failed</param>
/// <param name="AgentId">Agent definition ID. Defaults to "default" if not specified.</param>
public record ClientToolResponseRequest(
    string? SessionId,
    string RequestId,
    bool Success,
    List<ClientToolContentDto>? Content,
    string? ErrorMessage,
    string? AgentId = null);
