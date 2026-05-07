using HPDAgent.Graph.Abstractions.Storage;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Discovery;
using HPDAgent.Graph.Abstractions.Execution;
using System.Text.Json;

namespace HPDAgent.Graph.Hosting.Data;

public sealed record CreateWorkflowRequest
{
    public required GraphConfig Config { get; init; }
}

public sealed record UpdateWorkflowRequest
{
    public required GraphConfig Config { get; init; }
}

public sealed record WorkflowDto
{
    public required string GraphId { get; init; }
    public required string Name { get; init; }
    public required string GraphVersion { get; init; }
    public GraphConfig? Config { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? Description { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record WorkflowListResponse
{
    public required IReadOnlyList<StoredGraphSummary> Workflows { get; init; }
}

public sealed record HandlerCatalogResponse
{
    public required IReadOnlyDictionary<string, HandlerDescriptor> Handlers { get; init; }
}

public sealed record CreateScheduleRequest
{
    public required GraphScheduleConfig Schedule { get; init; }
    public bool Enabled { get; init; } = true;
}

public sealed record UpdateScheduleRequest
{
    public required GraphScheduleConfig Schedule { get; init; }
    public bool? Enabled { get; init; }
}

public sealed record ScheduledGraphDto
{
    public required string GraphId { get; init; }
    public required GraphScheduleConfig Schedule { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? LastRunAt { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ScheduledGraphListResponse
{
    public required IReadOnlyList<ScheduledGraphDto> Schedules { get; init; }
}

public sealed record ExecuteWorkflowRequest
{
    public string? ExecutionId { get; init; }
    public JsonElement? Input { get; init; }
    public TimeSpan? Timeout { get; init; }
    public string? TriggeredBy { get; init; }
    public WorkflowExecutionMode Mode { get; init; } = WorkflowExecutionMode.Background;
    public bool StartImmediately { get; init; } = true;
}

public enum WorkflowExecutionMode
{
    Foreground,
    Background
}

public sealed record WorkflowExecutionDto
{
    public required string GraphId { get; init; }
    public required string ExecutionId { get; init; }
    public required WorkflowExecutionStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public JsonElement? Input { get; init; }
    public TimeSpan? Timeout { get; init; }
    public DateTimeOffset? DeadlineAt { get; init; }
    public string? TriggeredBy { get; init; }
    public string? CurrentNodeId { get; init; }
    public string? SuspendedNodeId { get; init; }
    public string? SuspendToken { get; init; }
    public SuspendReason? SuspendReason { get; init; }
    public string? SuspensionMessage { get; init; }
    public DateTimeOffset? SuspendedAt { get; init; }
    public string? ClaimedBy { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public DateTimeOffset? LeaseUntil { get; init; }
    public DateTimeOffset? LastHeartbeatAt { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record WorkflowStatusDto
{
    public required string GraphId { get; init; }
    public required string ExecutionId { get; init; }
    public required WorkflowExecutionStatus Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public JsonElement? Input { get; init; }
    public TimeSpan? Timeout { get; init; }
    public DateTimeOffset? DeadlineAt { get; init; }
    public string? TriggeredBy { get; init; }
    public string? CurrentNodeId { get; init; }
    public string? SuspendedNodeId { get; init; }
    public string? SuspendToken { get; init; }
    public SuspendReason? SuspendReason { get; init; }
    public string? SuspensionMessage { get; init; }
    public DateTimeOffset? SuspendedAt { get; init; }
    public string? ClaimedBy { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public DateTimeOffset? LeaseUntil { get; init; }
    public DateTimeOffset? LastHeartbeatAt { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record GraphLogEntryDto
{
    public DateTimeOffset Timestamp { get; init; }
    public required string Level { get; init; }
    public required string Source { get; init; }
    public required string Message { get; init; }
    public string? NodeId { get; init; }
    public string? Exception { get; init; }
}

public sealed record SuspendedNodeDto
{
    public required string GraphId { get; init; }
    public required string ExecutionId { get; init; }
    public required string NodeId { get; init; }
    public required string SuspendToken { get; init; }
    public SuspendReason? Reason { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset? SuspendedAt { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public TimeSpan? MaxWaitTime { get; init; }
    public int? MaxRetries { get; init; }
    public required WorkflowExecutionStatus Status { get; init; }
}

public sealed record PollingStatusDto
{
    public required string GraphId { get; init; }
    public required string ExecutionId { get; init; }
    public required string SuspendToken { get; init; }
    public required string NodeId { get; init; }
    public WorkflowExecutionStatus Status { get; init; }
    public required int AttemptNumber { get; init; }
    public required TimeSpan RetryAfter { get; init; }
    public required TimeSpan MaxWaitTime { get; init; }
    public required TimeSpan ElapsedTime { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
}

public sealed record ResumeSuspensionRequest
{
    public object? ResumeValue { get; init; }
}

public sealed record ResumeSuspensionResultDto
{
    public required string GraphId { get; init; }
    public required string ExecutionId { get; init; }
    public string? NodeId { get; init; }
    public required string SuspendToken { get; init; }
    public required ResumeSuspensionStatus Status { get; init; }
    public string? Message { get; init; }
}

public enum ResumeSuspensionStatus
{
    Accepted,
    AlreadyCompleted,
    NotFound,
    Rejected,
    Failed
}

public static class WorkflowDtoMapper
{
    public static ScheduledGraphDto ToScheduledGraphDto(ScheduledGraph scheduled) => new()
    {
        GraphId = scheduled.GraphId,
        Schedule = scheduled.Schedule,
        Enabled = scheduled.Enabled,
        CreatedAt = scheduled.CreatedAt,
        UpdatedAt = scheduled.UpdatedAt,
        LastRunAt = scheduled.LastRunAt,
        NextRunAt = scheduled.NextRunAt,
        Metadata = scheduled.Metadata
    };

    public static WorkflowDto ToWorkflowDto(StoredGraph graph) => new()
    {
        GraphId = graph.GraphId,
        Name = graph.Name,
        GraphVersion = graph.GraphVersion,
        Config = graph.Config,
        CreatedAt = graph.CreatedAt,
        UpdatedAt = graph.UpdatedAt,
        Description = graph.Description,
        Metadata = graph.Metadata
    };

    public static WorkflowExecutionDto ToExecutionDto(WorkflowExecution execution) => new()
    {
        GraphId = execution.GraphId,
        ExecutionId = execution.ExecutionId,
        Status = execution.Status,
        CreatedAt = execution.CreatedAt,
        StartedAt = execution.StartedAt,
        CompletedAt = execution.CompletedAt,
        Input = execution.Input,
        Timeout = execution.Timeout,
        DeadlineAt = execution.DeadlineAt,
        TriggeredBy = execution.TriggeredBy,
        CurrentNodeId = execution.CurrentNodeId,
        SuspendedNodeId = execution.SuspendedNodeId,
        SuspendToken = execution.SuspendToken,
        SuspendReason = execution.SuspendReason,
        SuspensionMessage = execution.SuspensionMessage,
        SuspendedAt = execution.SuspendedAt,
        ClaimedBy = execution.ClaimedBy,
        ClaimedAt = execution.ClaimedAt,
        LeaseUntil = execution.LeaseUntil,
        LastHeartbeatAt = execution.LastHeartbeatAt,
        AttemptCount = execution.AttemptCount,
        LastAttemptAt = execution.LastAttemptAt,
        NextAttemptAt = execution.NextAttemptAt,
        ErrorMessage = execution.ErrorMessage
    };

    public static WorkflowStatusDto ToStatusDto(WorkflowExecution execution) => new()
    {
        GraphId = execution.GraphId,
        ExecutionId = execution.ExecutionId,
        Status = execution.Status,
        CreatedAt = execution.CreatedAt,
        StartedAt = execution.StartedAt,
        CompletedAt = execution.CompletedAt,
        Input = execution.Input,
        Timeout = execution.Timeout,
        DeadlineAt = execution.DeadlineAt,
        TriggeredBy = execution.TriggeredBy,
        CurrentNodeId = execution.CurrentNodeId,
        SuspendedNodeId = execution.SuspendedNodeId,
        SuspendToken = execution.SuspendToken,
        SuspendReason = execution.SuspendReason,
        SuspensionMessage = execution.SuspensionMessage,
        SuspendedAt = execution.SuspendedAt,
        ClaimedBy = execution.ClaimedBy,
        ClaimedAt = execution.ClaimedAt,
        LeaseUntil = execution.LeaseUntil,
        LastHeartbeatAt = execution.LastHeartbeatAt,
        AttemptCount = execution.AttemptCount,
        LastAttemptAt = execution.LastAttemptAt,
        NextAttemptAt = execution.NextAttemptAt,
        ErrorMessage = execution.ErrorMessage
    };

    public static GraphLogEntryDto ToLogDto(WorkflowLogEntry entry) => new()
    {
        Timestamp = entry.Timestamp,
        Level = entry.Level.ToString(),
        Source = entry.Source,
        Message = entry.Message,
        NodeId = entry.NodeId,
        Exception = entry.Exception
    };
}
