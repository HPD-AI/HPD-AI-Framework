namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Options for backfilling artifacts across multiple partitions (Phase 3: Demand-Driven Execution).
/// Controls parallel processing, skip behavior, and progress reporting.
/// </summary>
public record BackfillOptions
{
    /// <summary>
    /// Maximum number of partitions to materialize in parallel.
    /// Default: 10.
    /// </summary>
    public int MaxParallelPartitions { get; init; } = 10;

    /// <summary>
    /// Skip partitions that are already materialized with the current version.
    /// Default: true (skip up-to-date partitions).
    /// </summary>
    public bool SkipExisting { get; init; } = true;

    /// <summary>
    /// Stop backfill on first partition failure.
    /// Default: false (continue processing remaining partitions).
    /// </summary>
    public bool FailFast { get; init; } = false;

    /// <summary>
    /// Emit progress events during backfill.
    /// Default: true (emit BackfillProgressEvent).
    /// </summary>
    public bool EmitProgressEvents { get; init; } = true;
}
