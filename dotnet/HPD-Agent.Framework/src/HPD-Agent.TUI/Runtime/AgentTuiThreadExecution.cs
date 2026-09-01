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
    string? ErrorMessage = null,
    IReadOnlyList<AgentTuiOperation>? Operations = null);

/// <summary>Projects one unified agent operation for terminal user interfaces.</summary>
public sealed record AgentTuiOperation(
    string OperationId,
    string? ProviderOperationId,
    string Name,
    string SourceKind,
    string ProviderStatus,
    string ObservationStatus,
    string ControlKind,
    string ControlCapabilities,
    string? ControlHandleId,
    long Version,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt,
    string? CompletionSummary,
    IReadOnlyList<string>? ArtifactReferences,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyDictionary<string, string>? Metadata);
