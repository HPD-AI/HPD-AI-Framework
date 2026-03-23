using HPDAgent.Graph.Abstractions.Artifacts;

namespace HPDAgent.Graph.Abstractions.Caching;

/// <summary>
/// Snapshot of graph execution state for incremental detection.
/// Stores fingerprints from previous execution to detect changes.
/// </summary>
public sealed record GraphSnapshot
{
    /// <summary>
    /// Fingerprint for each node from previous execution.
    /// Format: nodeId → fingerprint hash
    /// </summary>
    public required Dictionary<string, string> NodeFingerprints { get; init; }

    /// <summary>
    /// Global graph hash (graph structure + config).
    /// If this changes, entire graph must re-execute.
    /// </summary>
    public required string GraphHash { get; init; }

    /// <summary>
    /// Timestamp of snapshot creation.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Execution ID this snapshot was created from.
    /// </summary>
    public string? ExecutionId { get; init; }

    /// <summary>
    /// Partition snapshots for each partitioned node from previous execution.
    /// Format: nodeId → PartitionSnapshot
    /// Used to detect when partition definitions change (e.g., new regions added, time range expanded).
    /// When a partition snapshot hash changes, the node and all downstream nodes must re-execute.
    /// </summary>
    public IReadOnlyDictionary<string, PartitionSnapshot> PartitionSnapshots { get; init; }
        = new Dictionary<string, PartitionSnapshot>();
}
