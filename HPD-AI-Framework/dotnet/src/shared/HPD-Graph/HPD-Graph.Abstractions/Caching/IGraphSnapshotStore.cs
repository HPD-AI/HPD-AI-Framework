namespace HPDAgent.Graph.Abstractions.Caching;

/// <summary>
/// Storage for graph execution snapshots (for incremental execution).
/// Snapshots capture node fingerprints at a point in time, enabling change detection.
/// </summary>
public interface IGraphSnapshotStore
{
    /// <summary>
    /// Get the latest snapshot for a graph.
    /// Used by AffectedNodeDetector to compare current vs previous fingerprints.
    /// Returns null if no previous snapshot exists (first execution).
    /// </summary>
    Task<GraphSnapshot?> GetLatestSnapshotAsync(string graphId, CancellationToken ct = default);

    /// <summary>
    /// Save a snapshot after graph execution.
    /// Called by orchestrator after successful execution.
    /// </summary>
    Task SaveSnapshotAsync(string graphId, GraphSnapshot snapshot, CancellationToken ct = default);

    /// <summary>
    /// List all snapshots for a graph (for debugging/audit).
    /// Returns snapshots in reverse chronological order.
    /// </summary>
    IAsyncEnumerable<GraphSnapshot> ListSnapshotsAsync(string graphId, CancellationToken ct = default);

    /// <summary>
    /// Delete old snapshots based on retention policy.
    /// Prevents unbounded growth of snapshot history.
    /// Recommended: Keep last 10 snapshots or 30 days, whichever is longer.
    /// </summary>
    Task PruneOldSnapshotsAsync(string graphId, int keepLastN = 10, CancellationToken ct = default);
}
