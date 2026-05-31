namespace HPD.Agent.Hosting.Data;

public sealed record BranchRunDto(
    string RuntimeRunId,
    string AgentId,
    string SessionId,
    string BranchId,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    BranchRunErrorDto? Error,
    BranchRunBackgroundOperationDto? BackgroundOperation,
    IReadOnlyList<BranchRunBackgroundTaskDto> BackgroundTasks);

public sealed record BranchRunErrorDto(
    string? Type,
    string? Message);

public sealed record BranchRunBackgroundOperationDto(
    string Status,
    string? OperationId,
    string? StatusMessage,
    string? ContinuationToken);

public sealed record BranchRunBackgroundTaskDto(
    string TaskId,
    string Name,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? FaultedAt,
    string? ErrorType,
    string? ErrorMessage);
