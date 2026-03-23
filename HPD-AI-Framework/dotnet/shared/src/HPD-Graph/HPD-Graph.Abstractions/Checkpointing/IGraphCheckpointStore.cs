namespace HPDAgent.Graph.Abstractions.Checkpointing;

/// <summary>
/// Storage interface for graph checkpoints.
/// Implementations can use in-memory, SQLite, blob storage, etc.
/// </summary>
public interface IGraphCheckpointStore
{
    /// <summary>
    /// How many checkpoints to retain per execution.
    /// </summary>
    CheckpointRetentionMode RetentionMode { get; }

    /// <summary>
    /// Save a checkpoint.
    /// </summary>
    Task SaveCheckpointAsync(GraphCheckpoint checkpoint, CancellationToken ct = default);

    /// <summary>
    /// Load the most recent checkpoint for an execution.
    /// Returns null if no checkpoint exists.
    /// </summary>
    Task<GraphCheckpoint?> LoadLatestCheckpointAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// Load a specific checkpoint by ID.
    /// </summary>
    Task<GraphCheckpoint?> LoadCheckpointAsync(string checkpointId, CancellationToken ct = default);

    /// <summary>
    /// Delete all checkpoints for an execution.
    /// Called after successful completion or explicit cleanup.
    /// </summary>
    Task DeleteCheckpointsAsync(string executionId, CancellationToken ct = default);

    /// <summary>
    /// List all checkpoints for an execution, ordered by creation time.
    /// </summary>
    Task<IReadOnlyList<GraphCheckpoint>> ListCheckpointsAsync(string executionId, CancellationToken ct = default);
}

/// <summary>
/// How many checkpoints to keep per execution.
/// </summary>
public enum CheckpointRetentionMode
{
    /// <summary>
    /// Keep only the latest checkpoint per execution.
    /// RECOMMENDED for production (saves storage).
    /// </summary>
    LatestOnly,

    /// <summary>
    /// Keep all checkpoints (full history).
    /// Useful for debugging, audit trails, time-travel.
    /// </summary>
    FullHistory
}
