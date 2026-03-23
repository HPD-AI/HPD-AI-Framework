namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Partial result emitted during graph execution.
/// Represents completion of a single node.
/// </summary>
public sealed record PartialResult
{
    /// <summary>
    /// ID of the node that just completed.
    /// </summary>
    public required string CompletedNodeId { get; init; }

    /// <summary>
    /// Name of the handler that executed.
    /// </summary>
    public required string HandlerName { get; init; }

    /// <summary>
    /// Execution layer index (null for nodes outside layer-based execution).
    /// </summary>
    public int? LayerIndex { get; init; }

    /// <summary>
    /// Progress percentage (0.0 to 1.0).
    /// Based on number of completed nodes / total nodes.
    /// </summary>
    public float Progress { get; init; }

    /// <summary>
    /// Outputs from the completed node (null if IncludeOutputs = false).
    /// </summary>
    public IReadOnlyDictionary<string, object>? Outputs { get; init; }

    /// <summary>
    /// Time taken to execute the node.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Timestamp when the node completed.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Execution result status (Success, Failure, Skipped, etc.).
    /// </summary>
    public NodeExecutionResult Result { get; init; } = null!;
}
