namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Policy for cloning messages during output propagation.
/// Affects memory usage and performance.
/// Default: LazyClone (optimal for most workloads).
/// </summary>
public enum CloningPolicy
{
    /// <summary>
    /// Always clone outputs before propagation.
    /// Ensures downstream nodes cannot mutate upstream state.
    /// Use when handlers may mutate inputs.
    /// Memory: Highest (clones for every edge)
    /// Performance: Slowest (clones even for single-recipient)
    /// Safety: Safest (complete isolation)
    /// </summary>
    AlwaysClone,

    /// <summary>
    /// Never clone (fastest, least memory, but requires immutable handlers).
    /// Downstream nodes share references to same objects.
    /// ONLY use if handlers are side-effect free on inputs.
    /// Memory: Lowest (zero clones, shared references)
    /// Performance: Fastest (zero cloning overhead)
    /// Safety: Unsafe (mutations affect all recipients)
    /// </summary>
    NeverClone,

    /// <summary>
    /// Clone only when fanning out to multiple edges (lazy cloning).
    /// First downstream edge gets original, subsequent get clones.
    /// Optimizes for common single-recipient case.
    /// DEFAULT from v1.0 (optimal for most workloads).
    /// Memory: Medium (clones only when needed)
    /// Performance: Good (zero copy for single-recipient)
    /// Safety: Safe (isolated after first recipient)
    /// </summary>
    LazyClone
}
