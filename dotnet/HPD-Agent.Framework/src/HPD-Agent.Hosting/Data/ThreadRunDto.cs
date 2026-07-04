namespace HPD.Agent.Hosting.Data;

public sealed record ThreadRunDto(
    string RuntimeRunId,
    string AgentId,
    string SessionId,
    string ThreadId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    ThreadRunErrorDto? Error,
    ThreadRunModelBackgroundOperationDto? ModelBackgroundOperation,
    IReadOnlyList<ThreadRunBackgroundTaskDto> BackgroundTasks,
    IReadOnlyList<ThreadRunBackgroundHandleDto> BackgroundHandles);

public sealed record ThreadRunErrorDto(
    string? Type,
    string? Message);

public sealed record ThreadRunModelBackgroundOperationDto(
    string Status,
    string? OperationId,
    string? StatusMessage,
    string? ContinuationToken);

public sealed record ThreadRunBackgroundTaskDto(
    string TaskId,
    string Name,
    string SourceKind,
    string? SourceId,
    ThreadRunBackgroundTaskNotificationDto Notification,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? FaultedAt,
    string? ErrorType,
    string? ErrorMessage);

/// <summary>
/// API DTO projection of a background task notification rule.
/// </summary>
/// <param name="Kind">Rule kind, such as none, on_final_state, or strategy.</param>
/// <param name="StrategyName">Strategy name when <paramref name="Kind"/> is strategy.</param>
public sealed record ThreadRunBackgroundTaskNotificationDto(
    string Kind,
    string? StrategyName = null);

public sealed record ThreadRunBackgroundHandleDto(
    string HandleId,
    string Name,
    string HandleKind,
    string SourceKind,
    string? SourceId,
    string Status,
    string SupportedOperations,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyDictionary<string, string>? Metadata);
