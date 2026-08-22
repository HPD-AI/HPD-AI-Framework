namespace HPD.Graph.Abstractions.Checkpointing;

/// <summary>Collects checkpoints within one bounded graph execution.</summary>
/// <remarks>
/// This interface is execution-local semantic plumbing, not durable storage authority.
/// Hosted execution persists a selected checkpoint and creates its resume activation
/// atomically through HPD.Base after graph execution returns.
/// </remarks>
public interface IGraphCheckpointBuffer
{
    /// <summary>Gets the execution-local retention policy.</summary>
    CheckpointRetentionMode RetentionMode { get; }
    /// <summary>Records a checkpoint in the current execution buffer.</summary>
    Task SaveCheckpointAsync(GraphCheckpoint checkpoint, CancellationToken ct = default);
    /// <summary>Loads the latest buffered checkpoint for an execution.</summary>
    Task<GraphCheckpoint?> LoadLatestCheckpointAsync(string executionId, CancellationToken ct = default);
    /// <summary>Loads one buffered checkpoint by identity.</summary>
    Task<GraphCheckpoint?> LoadCheckpointAsync(string checkpointId, CancellationToken ct = default);
    /// <summary>Deletes buffered checkpoints for an execution.</summary>
    Task DeleteCheckpointsAsync(string executionId, CancellationToken ct = default);
    /// <summary>Lists buffered checkpoints in creation order.</summary>
    Task<IReadOnlyList<GraphCheckpoint>> ListCheckpointsAsync(string executionId, CancellationToken ct = default);
}

/// <summary>Controls execution-local checkpoint retention.</summary>
public enum CheckpointRetentionMode
{
    /// <summary>Keeps only the latest checkpoint per execution.</summary>
    LatestOnly,
    /// <summary>Keeps every checkpoint produced by the bounded execution.</summary>
    FullHistory,
}
