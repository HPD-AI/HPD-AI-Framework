namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Immutable snapshot of resolved partitions.
/// Framework uses this for fingerprinting and caching.
/// Returned by PartitionDefinition.ResolveAsync() to provide both partition keys
/// and a stable hash for incremental execution.
/// </summary>
public record PartitionSnapshot
{
    /// <summary>
    /// Resolved partition keys for this snapshot.
    /// These are the actual partition keys that will be used for execution.
    /// </summary>
    public required IReadOnlyList<PartitionKey> Keys { get; init; }

    /// <summary>
    /// Stable hash representing this partition configuration.
    /// Framework uses this for incremental execution fingerprinting.
    ///
    /// IMPORTANT: Different strategies have different trade-offs:
    /// - Stable (hash config only): Won't invalidate when data changes
    /// - Snapshot (hash config + keys): Invalidates when partition keys change
    /// - Fresh (include timestamp): Always recompute (defeats incremental execution)
    ///
    /// The framework uses this hash in the hierarchical fingerprinting system to determine
    /// if downstream nodes need recomputation when partition definitions change.
    /// </summary>
    public required string SnapshotHash { get; init; }

    /// <summary>
    /// Timestamp when this snapshot was created.
    /// Used for debugging and auditing purposes.
    /// Not used in fingerprinting (use SnapshotStrategy.AlwaysFresh if time-based invalidation is needed).
    /// </summary>
    public DateTimeOffset ResolvedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of partition keys in this snapshot.
    /// Convenience property for logging and diagnostics.
    /// </summary>
    public int KeyCount => Keys.Count;
}
