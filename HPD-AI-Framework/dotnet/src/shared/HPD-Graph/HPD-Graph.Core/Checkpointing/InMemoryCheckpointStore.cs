using HPDAgent.Graph.Abstractions.Checkpointing;

namespace HPDAgent.Graph.Core.Checkpointing;

/// <summary>
/// In-memory implementation of IGraphCheckpointStore.
/// Useful for testing and development.
/// NOT suitable for production (data lost on process restart).
/// </summary>
public class InMemoryCheckpointStore : IGraphCheckpointStore
{
    private readonly Dictionary<string, List<GraphCheckpoint>> _checkpointsByExecutionId = new();
    private readonly Dictionary<string, GraphCheckpoint> _checkpointsById = new();
    private readonly object _lock = new();

    public CheckpointRetentionMode RetentionMode { get; init; } = CheckpointRetentionMode.LatestOnly;

    public Task SaveCheckpointAsync(GraphCheckpoint checkpoint, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // Store by ID for direct lookup
            _checkpointsById[checkpoint.CheckpointId] = checkpoint;

            // Store in execution's history
            if (!_checkpointsByExecutionId.TryGetValue(checkpoint.ExecutionId, out var checkpoints))
            {
                checkpoints = new List<GraphCheckpoint>();
                _checkpointsByExecutionId[checkpoint.ExecutionId] = checkpoints;
            }

            checkpoints.Add(checkpoint);

            // Apply retention policy
            if (RetentionMode == CheckpointRetentionMode.LatestOnly && checkpoints.Count > 1)
            {
                // Keep only the latest checkpoint
                var oldCheckpoints = checkpoints.Take(checkpoints.Count - 1).ToList();
                foreach (var old in oldCheckpoints)
                {
                    _checkpointsById.Remove(old.CheckpointId);
                }
                checkpoints.RemoveRange(0, checkpoints.Count - 1);
            }
        }

        return Task.CompletedTask;
    }

    public Task<GraphCheckpoint?> LoadLatestCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_checkpointsByExecutionId.TryGetValue(executionId, out var checkpoints) && checkpoints.Count > 0)
            {
                return Task.FromResult<GraphCheckpoint?>(checkpoints[^1]);
            }

            return Task.FromResult<GraphCheckpoint?>(null);
        }
    }

    public Task<GraphCheckpoint?> LoadCheckpointAsync(string checkpointId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _checkpointsById.TryGetValue(checkpointId, out var checkpoint);
            return Task.FromResult(checkpoint);
        }
    }

    public Task DeleteCheckpointsAsync(string executionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_checkpointsByExecutionId.TryGetValue(executionId, out var checkpoints))
            {
                foreach (var checkpoint in checkpoints)
                {
                    _checkpointsById.Remove(checkpoint.CheckpointId);
                }
                _checkpointsByExecutionId.Remove(executionId);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GraphCheckpoint>> ListCheckpointsAsync(string executionId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_checkpointsByExecutionId.TryGetValue(executionId, out var checkpoints))
            {
                return Task.FromResult<IReadOnlyList<GraphCheckpoint>>(checkpoints.ToList());
            }

            return Task.FromResult<IReadOnlyList<GraphCheckpoint>>(Array.Empty<GraphCheckpoint>());
        }
    }

    /// <summary>
    /// Clear all checkpoints (for testing).
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _checkpointsByExecutionId.Clear();
            _checkpointsById.Clear();
        }
    }
}
