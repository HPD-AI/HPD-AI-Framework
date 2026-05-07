namespace HPDAgent.Graph.Abstractions.Storage;

using System.Text.Json;
using HPDAgent.Graph.Abstractions.Execution;

public interface IWorkflowExecutionStore
{
    Task SaveAsync(WorkflowExecution execution, CancellationToken ct = default);
    Task<WorkflowExecution?> LoadAsync(string graphId, string executionId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> ListAsync(string graphId, CancellationToken ct = default);
    Task<WorkflowExecution?> TryClaimAsync(
        string graphId,
        string executionId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken ct = default);

    Task<WorkflowExecution?> RenewLeaseAsync(
        string graphId,
        string executionId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken ct = default);

    Task ReleaseClaimAsync(
        string graphId,
        string executionId,
        string workerId,
        CancellationToken ct = default);
}

public interface IWorkflowSuspensionSink
{
    Task MarkSuspendedAsync(
        string graphId,
        string executionId,
        string nodeId,
        string suspendToken,
        SuspendReason reason,
        string? message = null,
        TimeSpan? retryAfter = null,
        TimeSpan? maxWaitTime = null,
        int? maxRetries = null,
        int? pollingAttemptNumber = null,
        CancellationToken ct = default);
}

public interface IWorkflowExecutionStateSink : IWorkflowSuspensionSink
{
    Task MarkRunningAsync(
        string graphId,
        string executionId,
        string? currentNodeId = null,
        CancellationToken ct = default);

    Task MarkFailedAsync(
        string graphId,
        string executionId,
        string? nodeId,
        string errorMessage,
        CancellationToken ct = default);
}

public sealed record WorkflowExecution
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
    public TimeSpan? RetryAfter { get; init; }
    public TimeSpan? MaxWaitTime { get; init; }
    public int? MaxRetries { get; init; }
    public int? PollingAttemptNumber { get; init; }
    public DateTimeOffset? PollingStartedAt { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? ClaimedBy { get; init; }
    public DateTimeOffset? ClaimedAt { get; init; }
    public DateTimeOffset? LeaseUntil { get; init; }
    public DateTimeOffset? LastHeartbeatAt { get; init; }
    public int AttemptCount { get; init; }
    public DateTimeOffset? LastAttemptAt { get; init; }
    public DateTimeOffset? NextAttemptAt { get; init; }
    public IReadOnlyList<WorkflowSuspension> Suspensions { get; init; } = Array.Empty<WorkflowSuspension>();
    public string? ErrorMessage { get; init; }
}

public sealed record WorkflowSuspension
{
    public required string NodeId { get; init; }
    public required string SuspendToken { get; init; }
    public required SuspendReason Reason { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset SuspendedAt { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public TimeSpan? MaxWaitTime { get; init; }
    public int? MaxRetries { get; init; }
    public int? PollingAttemptNumber { get; init; }
    public DateTimeOffset? PollingStartedAt { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
}

public enum WorkflowExecutionStatus
{
    Created,
    Running,
    Suspended,
    Polling,
    Completed,
    Failed,
    Cancelled
}
