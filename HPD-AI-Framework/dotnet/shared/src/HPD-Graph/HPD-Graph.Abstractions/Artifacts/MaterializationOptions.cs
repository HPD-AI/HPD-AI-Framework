namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Options for artifact materialization (Phase 3: Demand-Driven Execution).
/// Controls how artifacts are computed and cached.
/// </summary>
public record MaterializationOptions
{
    /// <summary>
    /// Force re-computation even if cached version exists.
    /// Default: false (use cache if available and up-to-date).
    /// </summary>
    public bool ForceRecompute { get; init; } = false;

    /// <summary>
    /// Wait for distributed lock if artifact is being materialized by another process.
    /// Default: true (wait for lock acquisition).
    /// </summary>
    public bool WaitForLock { get; init; } = true;

    /// <summary>
    /// Timeout for acquiring materialization lock.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan LockTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
