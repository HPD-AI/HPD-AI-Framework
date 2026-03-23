namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Configuration for streaming execution.
/// </summary>
public sealed record StreamingOptions
{
    /// <summary>
    /// Include node outputs in partial results.
    /// Default: false (lower overhead for progress tracking only).
    /// Set to true if you need to inspect intermediate results.
    /// </summary>
    public bool IncludeOutputs { get; init; }

    /// <summary>
    /// When to emit partial results.
    /// </summary>
    public PartialResultEmissionMode EmissionMode { get; init; } = PartialResultEmissionMode.EveryNode;
}

/// <summary>
/// Mode for emitting partial results during streaming execution.
/// </summary>
public enum PartialResultEmissionMode
{
    /// <summary>
    /// Emit a result after every node completes.
    /// Use for fine-grained progress tracking.
    /// </summary>
    EveryNode,

    /// <summary>
    /// Emit results only at layer boundaries (after all nodes in a layer complete).
    /// Use for coarse-grained progress with lower overhead.
    /// </summary>
    LayerBoundaries
}
