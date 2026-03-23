namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Defines how errors propagate through the graph.
/// Configured per-node or globally.
/// Universal primitive for ANY workflow.
/// </summary>
public sealed record ErrorPropagationPolicy
{
    /// <summary>
    /// How should the orchestrator respond when this node fails?
    /// </summary>
    public required PropagationMode Mode { get; init; }

    /// <summary>
    /// Which downstream nodes are affected by this node's failure?
    /// Null = all downstream (default).
    /// Empty = none (isolated failure).
    /// Specific list = only these nodes.
    /// </summary>
    public IReadOnlyList<string>? AffectedNodes { get; init; }

    /// <summary>
    /// Fallback node to execute if this node fails.
    /// Example: Primary API fails → fallback to cache.
    /// Used when Mode = ExecuteFallback.
    /// </summary>
    public string? FallbackNodeId { get; init; }

    /// <summary>
    /// Custom predicate to filter which errors propagate.
    /// Null = all errors propagate (default).
    /// Example: Only propagate DatabaseConnectionError, ignore others.
    /// </summary>
    public Func<Exception, bool>? ShouldPropagate { get; init; }

    /// <summary>
    /// Creates a policy that stops the entire graph on error.
    /// </summary>
    public static ErrorPropagationPolicy StopGraph() => new() { Mode = PropagationMode.StopGraph };

    /// <summary>
    /// Creates a policy that skips dependent nodes on error.
    /// </summary>
    public static ErrorPropagationPolicy SkipDependents(IReadOnlyList<string>? affectedNodes = null) => new()
    {
        Mode = PropagationMode.SkipDependents,
        AffectedNodes = affectedNodes
    };

    /// <summary>
    /// Creates a policy that executes a fallback node on error.
    /// </summary>
    public static ErrorPropagationPolicy ExecuteFallback(string fallbackNodeId) => new()
    {
        Mode = PropagationMode.ExecuteFallback,
        FallbackNodeId = fallbackNodeId
            ?? throw new ArgumentNullException(nameof(fallbackNodeId), "Fallback node ID cannot be null")
    };

    /// <summary>
    /// Creates a policy that isolates errors (continues execution).
    /// </summary>
    public static ErrorPropagationPolicy Isolate() => new() { Mode = PropagationMode.Isolate };
}
