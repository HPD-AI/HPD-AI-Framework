using System.Text.Json;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.Abstractions.Storage;

namespace HPD.Graph.Core.Storage;

/// <summary>
/// File-backed workflow execution status store using one JSON file per execution.
/// </summary>
public sealed class JsonWorkflowExecutionStore : IWorkflowExecutionStore
{
    private readonly string _executionsDirectory;

    public JsonWorkflowExecutionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _executionsDirectory = Path.Combine(rootDirectory, "executions");
    }

    public async Task SaveAsync(WorkflowExecution execution, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.GraphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.ExecutionId);
        ct.ThrowIfCancellationRequested();

        var graphDirectory = GetGraphDirectory(execution.GraphId);
        Directory.CreateDirectory(graphDirectory);

        var path = GetExecutionPath(execution.GraphId, execution.ExecutionId);
        await WriteJsonAsync(path, execution, ct);
    }

    public async Task<WorkflowExecution?> LoadAsync(string graphId, string executionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ct.ThrowIfCancellationRequested();

        var path = GetExecutionPath(graphId, executionId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            GraphConfigJsonSerializerContext.Default.WorkflowExecution,
            ct);
    }

    public async Task<IReadOnlyList<WorkflowExecution>> ListAsync(string graphId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ct.ThrowIfCancellationRequested();

        var graphDirectory = GetGraphDirectory(graphId);
        if (!Directory.Exists(graphDirectory))
        {
            return Array.Empty<WorkflowExecution>();
        }

        var executions = new List<WorkflowExecution>();
        foreach (var path in Directory.EnumerateFiles(graphDirectory, "*.execution.json").OrderBy(static p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(path);
            var execution = await JsonSerializer.DeserializeAsync(
                stream,
                GraphConfigJsonSerializerContext.Default.WorkflowExecution,
                ct);

            if (execution is not null)
            {
                executions.Add(execution);
            }
        }

        return executions
            .OrderBy(execution => execution.CreatedAt)
            .ThenBy(execution => execution.ExecutionId, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<WorkflowExecution?> TryClaimAsync(
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

        await using var lease = await AcquireFileLockAsync(graphId, executionId, ct).ConfigureAwait(false);
        var execution = await LoadUnderLockAsync(graphId, executionId, ct).ConfigureAwait(false);
        if (execution is null || !CanClaim(execution, workerId, now))
        {
            return null;
        }

        var claimed = Claim(execution, workerId, now, leaseDuration);
        await SaveUnderLockAsync(claimed, ct).ConfigureAwait(false);
        return claimed;
    }

    public async Task<WorkflowExecution?> RenewLeaseAsync(
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

        await using var lease = await AcquireFileLockAsync(graphId, executionId, ct).ConfigureAwait(false);
        var execution = await LoadUnderLockAsync(graphId, executionId, ct).ConfigureAwait(false);
        if (execution is null || !OwnsActiveLease(execution, workerId, now))
        {
            return null;
        }

        var renewed = execution with
        {
            LeaseUntil = now + leaseDuration,
            LastHeartbeatAt = now
        };

        await SaveUnderLockAsync(renewed, ct).ConfigureAwait(false);
        return renewed;
    }

    public async Task ReleaseClaimAsync(
        string graphId,
        string executionId,
        string workerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ct.ThrowIfCancellationRequested();

        await using var lease = await AcquireFileLockAsync(graphId, executionId, ct).ConfigureAwait(false);
        var execution = await LoadUnderLockAsync(graphId, executionId, ct).ConfigureAwait(false);
        if (execution is null || !string.Equals(execution.ClaimedBy, workerId, StringComparison.Ordinal))
        {
            return;
        }

        await SaveUnderLockAsync(execution with
        {
            ClaimedBy = null,
            ClaimedAt = null,
            LeaseUntil = null,
            LastHeartbeatAt = null
        }, ct).ConfigureAwait(false);
    }

    private string GetGraphDirectory(string graphId) =>
        Path.Combine(_executionsDirectory, EncodeFileName(graphId));

    private string GetExecutionPath(string graphId, string executionId) =>
        Path.Combine(GetGraphDirectory(graphId), $"{EncodeFileName(executionId)}.execution.json");

    private string GetLockPath(string graphId, string executionId) =>
        Path.Combine(GetGraphDirectory(graphId), $"{EncodeFileName(executionId)}.execution.lock");

    private async Task<FileStream> AcquireFileLockAsync(
        string graphId,
        string executionId,
        CancellationToken ct)
    {
        var graphDirectory = GetGraphDirectory(graphId);
        Directory.CreateDirectory(graphDirectory);
        var lockPath = GetLockPath(graphId, executionId);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<WorkflowExecution?> LoadUnderLockAsync(
        string graphId,
        string executionId,
        CancellationToken ct)
    {
        var path = GetExecutionPath(graphId, executionId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(
            stream,
            GraphConfigJsonSerializerContext.Default.WorkflowExecution,
            ct).ConfigureAwait(false);
    }

    private Task SaveUnderLockAsync(WorkflowExecution execution, CancellationToken ct)
    {
        var graphDirectory = GetGraphDirectory(execution.GraphId);
        Directory.CreateDirectory(graphDirectory);
        return WriteJsonAsync(GetExecutionPath(execution.GraphId, execution.ExecutionId), execution, ct);
    }

    private static async Task WriteJsonAsync(string path, WorkflowExecution execution, CancellationToken ct)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                execution,
                GraphConfigJsonSerializerContext.Default.WorkflowExecution,
                ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private static string EncodeFileName(string value) => Uri.EscapeDataString(value);

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
