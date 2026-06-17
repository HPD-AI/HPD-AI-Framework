namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiThreadRun(
    string RuntimeRunId,
    string AgentId,
    string SessionId,
    string ThreadId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    string? ErrorType = null,
    string? ErrorMessage = null);
