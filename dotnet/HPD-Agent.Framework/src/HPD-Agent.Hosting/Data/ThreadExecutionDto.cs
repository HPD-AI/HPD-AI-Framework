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
    ThreadExecutionModelBackgroundOperationDto? ModelBackgroundOperation,
    IReadOnlyList<ThreadExecutionBackgroundTaskDto> BackgroundTasks,
    IReadOnlyList<ThreadExecutionBackgroundHandleDto> BackgroundHandles);

public sealed record ThreadExecutionErrorDto(
    string? Type,
    string? Message);

public sealed record ThreadExecutionModelBackgroundOperationDto(
    string Status,
    string? OperationId,
    string? StatusMessage,
    string? ContinuationToken);

public sealed record ThreadExecutionBackgroundTaskDto(
    string TaskId,
    string Name,
    string SourceKind,
    string? SourceId,
    ThreadExecutionBackgroundTaskNotificationDto Notification,
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
public sealed record ThreadExecutionBackgroundTaskNotificationDto(
    string Kind,
    string? StrategyName = null);

public sealed record ThreadExecutionBackgroundHandleDto(
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
