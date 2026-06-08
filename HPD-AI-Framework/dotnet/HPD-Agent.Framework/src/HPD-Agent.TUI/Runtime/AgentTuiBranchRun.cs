namespace HPD.Agent.TUI.Runtime;

public sealed record AgentTuiBranchRun(
    string RuntimeRunId,
    string AgentId,
    string SessionId,
    string BranchId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    string? ErrorType = null,
    string? ErrorMessage = null);
