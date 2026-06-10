using HPD.Graph.Abstractions.Storage;

namespace HPD.Graph.Core.Storage;

/// <summary>
/// In-memory workflow execution store for development and tests.
/// </summary>
public sealed class InMemoryWorkflowExecutionStore : IWorkflowExecutionStore
{
    private readonly Dictionary<string, Dictionary<string, WorkflowExecution>> _executionsByGraphId =
        new(StringComparer.Ordinal);

    private readonly object _lock = new();

    public Task SaveAsync(WorkflowExecution execution, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.GraphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.ExecutionId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_executionsByGraphId.TryGetValue(execution.GraphId, out var executions))
            {
                executions = new Dictionary<string, WorkflowExecution>(StringComparer.Ordinal);
                _executionsByGraphId[execution.GraphId] = executions;
            }

            executions[execution.ExecutionId] = execution;
        }

        return Task.CompletedTask;
    }

    public Task<WorkflowExecution?> LoadAsync(string graphId, string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_executionsByGraphId.TryGetValue(graphId, out var executions) &&
                executions.TryGetValue(executionId, out var execution))
            {
                return Task.FromResult<WorkflowExecution?>(execution);
            }

            return Task.FromResult<WorkflowExecution?>(null);
        }
    }

    public Task<IReadOnlyList<WorkflowExecution>> ListAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_executionsByGraphId.TryGetValue(graphId, out var executions))
            {
                return Task.FromResult<IReadOnlyList<WorkflowExecution>>(Array.Empty<WorkflowExecution>());
            }

            var ordered = executions.Values
                .OrderBy(execution => execution.CreatedAt)
                .ThenBy(execution => execution.ExecutionId, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult<IReadOnlyList<WorkflowExecution>>(ordered);
        }
    }

    public Task<WorkflowExecution?> TryClaimAsync(
        string graphId,
        string executionId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_executionsByGraphId.TryGetValue(graphId, out var executions) ||
                !executions.TryGetValue(executionId, out var execution) ||
                !CanClaim(execution, workerId, now))
            {
                return Task.FromResult<WorkflowExecution?>(null);
            }

            var claimed = Claim(execution, workerId, now, leaseDuration);
            executions[executionId] = claimed;
            return Task.FromResult<WorkflowExecution?>(claimed);
        }
    }

    public Task<WorkflowExecution?> RenewLeaseAsync(
        string graphId,
        string executionId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_executionsByGraphId.TryGetValue(graphId, out var executions) ||
                !executions.TryGetValue(executionId, out var execution) ||
                !OwnsActiveLease(execution, workerId, now))
            {
                return Task.FromResult<WorkflowExecution?>(null);
            }

            var renewed = execution with
            {
                LeaseUntil = now + leaseDuration,
                LastHeartbeatAt = now
            };
            executions[executionId] = renewed;
            return Task.FromResult<WorkflowExecution?>(renewed);
        }
    }

    public Task ReleaseClaimAsync(
        string graphId,
        string executionId,
        string workerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_executionsByGraphId.TryGetValue(graphId, out var executions) &&
                executions.TryGetValue(executionId, out var execution) &&
                string.Equals(execution.ClaimedBy, workerId, StringComparison.Ordinal))
            {
                executions[executionId] = execution with
                {
                    ClaimedBy = null,
                    ClaimedAt = null,
                    LeaseUntil = null,
                    LastHeartbeatAt = null
                };
            }
        }

        return Task.CompletedTask;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _executionsByGraphId.Clear();
        }
    }

    private static bool CanClaim(WorkflowExecution execution, string workerId, DateTimeOffset now)
    {
        if (execution.Status is WorkflowExecutionStatus.Completed or
            WorkflowExecutionStatus.Failed or
            WorkflowExecutionStatus.Cancelled or
            WorkflowExecutionStatus.Suspended or
            WorkflowExecutionStatus.Polling)
        {
            return false;
        }

        if (execution.NextAttemptAt is { } nextAttemptAt && nextAttemptAt > now)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(execution.ClaimedBy) &&
            !string.Equals(execution.ClaimedBy, workerId, StringComparison.Ordinal) &&
            execution.LeaseUntil is { } leaseUntil &&
            leaseUntil > now)
        {
            return false;
        }

        return execution.Status is WorkflowExecutionStatus.Created or WorkflowExecutionStatus.Running;
    }

    private static bool OwnsActiveLease(WorkflowExecution execution, string workerId, DateTimeOffset now)
    {
        return string.Equals(execution.ClaimedBy, workerId, StringComparison.Ordinal) &&
               execution.LeaseUntil is { } leaseUntil &&
               leaseUntil > now &&
               execution.Status == WorkflowExecutionStatus.Running;
    }

    private static WorkflowExecution Claim(
        WorkflowExecution execution,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        var isSameWorker = string.Equals(execution.ClaimedBy, workerId, StringComparison.Ordinal);
        return execution with
        {
            Status = WorkflowExecutionStatus.Running,
            StartedAt = execution.StartedAt ?? now,
            ClaimedBy = workerId,
            ClaimedAt = isSameWorker ? execution.ClaimedAt ?? now : now,
            LeaseUntil = now + leaseDuration,
            LastHeartbeatAt = now,
            AttemptCount = isSameWorker ? execution.AttemptCount : execution.AttemptCount + 1,
            LastAttemptAt = isSameWorker ? execution.LastAttemptAt ?? now : now,
            NextAttemptAt = null
        };
    }
}
