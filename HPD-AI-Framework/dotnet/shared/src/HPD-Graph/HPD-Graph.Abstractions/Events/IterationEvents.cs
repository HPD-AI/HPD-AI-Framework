using HPD.Events;

namespace HPDAgent.Graph.Abstractions.Events;

/// <summary>
/// Event emitted at the start of each iteration in cyclic graph execution.
/// Only emitted for graphs with back-edges (cycles).
/// </summary>
public sealed record IterationStartedEvent : GraphEvent
{
    /// <summary>
    /// Zero-based iteration index.
    /// First iteration is 0.
    /// </summary>
    public required int IterationIndex { get; init; }

    /// <summary>
    /// Number of nodes scheduled for execution this iteration.
    /// </summary>
    public required int DirtyNodeCount { get; init; }

    /// <summary>
    /// IDs of nodes scheduled for execution this iteration.
    /// </summary>
    public required IReadOnlyList<string> DirtyNodeIds { get; init; }

    /// <summary>
    /// Number of execution layers this iteration.
    /// </summary>
    public required int LayerCount { get; init; }

    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted at the end of each iteration in cyclic graph execution.
/// </summary>
public sealed record IterationCompletedEvent : GraphEvent
{
    /// <summary>
    /// Zero-based iteration index.
    /// </summary>
    public required int IterationIndex { get; init; }

    /// <summary>
    /// Time taken for this iteration.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// IDs of nodes that executed this iteration.
    /// </summary>
    public required IReadOnlyList<string> ExecutedNodes { get; init; }

    /// <summary>
    /// Number of back-edges that triggered this iteration.
    /// </summary>
    public required int BackEdgesTriggered { get; init; }

    /// <summary>
    /// IDs of nodes scheduled for re-execution in next iteration.
    /// Empty if this is the final iteration.
    /// </summary>
    public required IReadOnlyList<string> NodesToReExecute { get; init; }

    /// <summary>
    /// True if this is the final iteration (no back-edges triggered).
    /// </summary>
    public bool IsFinalIteration => NodesToReExecute.Count == 0;

    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when a back-edge condition evaluates to true,
/// triggering re-execution of upstream nodes.
/// </summary>
public sealed record BackEdgeTriggeredEvent : GraphEvent
{
    /// <summary>
    /// ID of the source node (where the back-edge originates).
    /// </summary>
    public required string SourceNodeId { get; init; }

    /// <summary>
    /// ID of the target node (will be re-executed).
    /// </summary>
    public required string TargetNodeId { get; init; }

    /// <summary>
    /// Human-readable description of the condition that triggered.
    /// Null for unconditional back-edges.
    /// </summary>
    public string? ConditionDescription { get; init; }

    /// <summary>
    /// The output value that caused the condition to evaluate to true.
    /// </summary>
    public object? TriggerValue { get; init; }

    /// <summary>
    /// Iteration index when this back-edge fired.
    /// </summary>
    public required int IterationIndex { get; init; }

    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when maximum iterations is reached with dirty nodes remaining.
/// This indicates the graph did not converge within the iteration limit.
/// </summary>
public sealed record MaxIterationsReachedEvent : GraphEvent
{
    /// <summary>
    /// The maximum iterations limit that was reached.
    /// </summary>
    public required int MaxIterations { get; init; }

    /// <summary>
    /// IDs of nodes that still needed execution when limit was hit.
    /// </summary>
    public required IReadOnlyList<string> RemainingDirtyNodes { get; init; }

    /// <summary>
    /// String representations of back-edges that were still triggering.
    /// Format: "SourceNodeId->TargetNodeId"
    /// </summary>
    public required IReadOnlyList<string> ActiveBackEdges { get; init; }

    public new EventKind Kind { get; init; } = EventKind.Lifecycle;

    /// <summary>
    /// Control priority - this is a warning condition that should be noticed by operators.
    /// </summary>
    public new EventPriority Priority { get; init; } = EventPriority.Control;
}

/// <summary>
/// Event emitted when graph execution converges (no output changes detected).
/// Only emitted when change-aware iteration is enabled with auto-convergence.
/// </summary>
public sealed record GraphConvergedEvent : GraphEvent
{
    /// <summary>
    /// The iteration index when convergence was detected.
    /// </summary>
    public required int IterationIndex { get; init; }

    /// <summary>
    /// Total number of iterations executed (IterationIndex + 1).
    /// </summary>
    public required int TotalIterations { get; init; }

    /// <summary>
    /// Reason for convergence detection.
    /// </summary>
    public required string ConvergenceReason { get; init; }

    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when a node is skipped due to unchanged inputs during change-aware iteration.
/// Provides visibility into optimization decisions.
/// </summary>
public sealed record NodeSkippedUnchangedEvent : GraphEvent
{
    /// <summary>
    /// ID of the node that was skipped.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// The iteration index when the skip occurred.
    /// </summary>
    public required int IterationIndex { get; init; }

    /// <summary>
    /// Reason for skipping (e.g., "inputs_unchanged").
    /// </summary>
    public required string Reason { get; init; }

    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when a back-edge is skipped due to unchanged source output.
/// Only emitted when change-aware iteration is enabled.
/// </summary>
public sealed record BackEdgeSkippedEvent : GraphEvent
{
    /// <summary>
    /// ID of the source node (where the back-edge originates).
    /// </summary>
    public required string SourceNodeId { get; init; }

    /// <summary>
    /// ID of the target node (would have been re-executed).
    /// </summary>
    public required string TargetNodeId { get; init; }

    /// <summary>
    /// Reason for skipping (e.g., "output_unchanged").
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The iteration index when the skip occurred.
    /// </summary>
    public required int IterationIndex { get; init; }

    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}
