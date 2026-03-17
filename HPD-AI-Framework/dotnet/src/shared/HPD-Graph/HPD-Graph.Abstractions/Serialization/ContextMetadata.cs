namespace HPDAgent.Graph.Abstractions.Serialization;

/// <summary>
/// Metadata for context serialization in checkpoints.
/// Native AOT compatible record type (replaces anonymous types).
/// </summary>
public sealed record ContextMetadata
{
    /// <summary>
    /// Unique execution identifier.
    /// </summary>
    public required string ExecutionId { get; init; }

    /// <summary>
    /// Nodes that have completed execution.
    /// </summary>
    public required List<string> CompletedNodes { get; init; }

    /// <summary>
    /// Current layer index in topological execution order.
    /// </summary>
    public int CurrentLayerIndex { get; init; }

    /// <summary>
    /// Current iteration number (for cyclic graphs).
    /// </summary>
    public int CurrentIteration { get; init; }

    /// <summary>
    /// Nodes pending re-execution in next iteration.
    /// </summary>
    public required List<string> PendingDirtyNodes { get; init; }
}
