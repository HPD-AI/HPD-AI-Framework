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
    ThreadRunBackgroundOperationDto? BackgroundOperation,
    IReadOnlyList<ThreadRunBackgroundTaskDto> BackgroundTasks);

public sealed record ThreadRunErrorDto(
    string? Type,
    string? Message);

public sealed record ThreadRunBackgroundOperationDto(
    string Status,
    string? OperationId,
    string? StatusMessage,
    string? ContinuationToken);

public sealed record ThreadRunBackgroundTaskDto(
    string TaskId,
    string Name,
    string SourceKind,
    string? SourceId,
    string NotificationPolicy,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? FaultedAt,
    string? ErrorType,
    string? ErrorMessage);
