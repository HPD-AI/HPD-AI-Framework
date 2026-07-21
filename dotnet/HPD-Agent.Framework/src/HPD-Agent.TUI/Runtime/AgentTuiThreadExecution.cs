namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiThreadExecution(
    string ThreadExecutionId,
    string AgentId,
    string SessionId,
    string ThreadId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt = null,
    string? ErrorType = null,
    string? ErrorMessage = null);
