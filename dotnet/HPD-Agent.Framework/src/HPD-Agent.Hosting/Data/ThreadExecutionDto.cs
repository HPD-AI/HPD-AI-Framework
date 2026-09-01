namespace HPD.Agent.Hosting.Data;

public sealed record ThreadExecutionDto(
    string ThreadExecutionId,
    string AgentId,
    string SessionId,
    string ThreadId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    ThreadExecutionErrorDto? Error,
    IReadOnlyList<ThreadExecutionOperationDto> Operations);

public sealed record ThreadExecutionCancellationDto(
    string ThreadExecutionId,
    string Status,
    bool CancellationApplied,
    string QueuePromotion);

public sealed record ThreadExecutionErrorDto(
    string? Type,
    string? Message);

/// <summary>Projects one authoritative operation aggregate without runtime hooks or credentials.</summary>
public sealed record ThreadExecutionOperationDto(
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
