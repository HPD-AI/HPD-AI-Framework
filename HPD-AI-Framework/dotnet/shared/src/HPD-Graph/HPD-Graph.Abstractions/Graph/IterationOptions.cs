namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// Configuration options for graph iteration behavior.
/// Controls change-aware iteration, convergence detection, and dirty propagation.
/// </summary>
public sealed record IterationOptions
{
    /// <summary>
    /// Maximum iterations before forced stop.
    /// Overrides Graph.MaxIterations when specified.
    /// </summary>
    public int MaxIterations { get; init; } = 25;

    /// <summary>
    /// Enable change-aware iteration (output-hash based dirty detection).
    /// When true, only nodes with changed inputs re-execute during iterations.
    /// When false, uses eager propagation (marks all downstream nodes dirty).
    /// Default: false (opt-in for backward compatibility)
    /// </summary>
    public bool UseChangeAwareIteration { get; init; } = false;

    /// <summary>
    /// Enable automatic convergence detection.
    /// Stops iteration when no outputs change between iterations.
    /// Only effective when UseChangeAwareIteration is true.
    /// </summary>
    public bool EnableAutoConvergence { get; init; } = true;

    /// <summary>
    /// Output fields to exclude from change detection.
    /// Use for non-deterministic fields like timestamps, request IDs.
    /// Example: ["timestamp", "requestId", "traceId"]
    /// </summary>
    public HashSet<string>? IgnoreFieldsForChangeDetection { get; init; }

    /// <summary>
    /// Nodes that should always be considered "changed" and re-executed.
    /// Useful for debugging or forcing re-execution of specific nodes.
    /// </summary>
    public HashSet<string>? AlwaysDirtyNodes { get; init; }

    /// <summary>
    /// Hashing algorithm for output comparison.
    /// XxHash64 recommended for speed; SHA256 for verification scenarios.
    /// </summary>
    public OutputHashAlgorithm HashAlgorithm { get; init; } = OutputHashAlgorithm.XxHash64;
}

/// <summary>
/// Algorithm used for output hash computation in change-aware iteration.
/// </summary>
public enum OutputHashAlgorithm
{
    /// <summary>
    /// Fast non-cryptographic hash (recommended for within-iteration comparison).
    /// ~1 in 2^64 collision probability - effectively zero for iteration use.
    /// </summary>
    XxHash64,

    /// <summary>
    /// Cryptographic hash (slower, use for cross-execution caching or verification).
    /// </summary>
    SHA256
}
