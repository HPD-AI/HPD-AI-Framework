namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Strategy for computing snapshot hash in dynamic partitions.
/// Controls trade-off between stability and freshness in incremental execution.
///
/// This enum guides partition definition implementations on how to compute
/// the SnapshotHash field in PartitionSnapshot, which directly affects when
/// downstream nodes are recomputed.
/// </summary>
public enum SnapshotStrategy
{
    /// <summary>
    /// Stable hash based on configuration only (ignores data changes).
    ///
    /// Use when: Partitions are append-only or data changes don't require recomputation.
    /// Trade-off: Adding/removing partitions won't invalidate existing results.
    ///
    /// Example: Database query "SELECT customer_id FROM active_customers" with StableConfig
    /// will hash only the query string. If new customers are added, the hash stays the same,
    /// so incremental execution won't recompute downstream nodes.
    ///
    /// Best for: Append-only data pipelines where new partitions don't affect old results.
    /// </summary>
    StableConfig,

    /// <summary>
    /// Hash includes partition keys (invalidates when partition set changes).
    ///
    /// Use when: Adding/removing partitions should trigger recomputation.
    /// Trade-off: Balanced between stability and freshness.
    ///
    /// Example: S3 bucket listing with SnapshotKeys will hash both the bucket config
    /// and the actual file list. If a new file appears, the hash changes, triggering
    /// recomputation of downstream nodes.
    ///
    /// Best for: Data pipelines where the set of partitions itself determines correctness.
    /// </summary>
    SnapshotKeys,

    /// <summary>
    /// Always fresh (includes timestamp, defeats incremental execution).
    ///
    /// Use when: Results must always be recomputed regardless of changes.
    /// Trade-off: No incremental execution benefits - always recomputes.
    ///
    /// Example: Real-time monitoring query with AlwaysFresh will include the current
    /// timestamp in the hash, forcing recomputation every time the graph runs.
    ///
    /// Best for: Live data sources, polling sensors, or workflows that must run fresh every time.
    /// </summary>
    AlwaysFresh
}
