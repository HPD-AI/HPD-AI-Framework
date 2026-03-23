namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// Represents a back-edge (cycle-forming edge) in the graph.
/// A back-edge points from a node that appears LATER in topological order
/// to a node that appears EARLIER, creating a cycle.
/// </summary>
public sealed record BackEdge
{
    /// <summary>
    /// The underlying edge definition.
    /// </summary>
    public required Edge Edge { get; init; }

    /// <summary>
    /// Topological order index of the source node.
    /// Higher value means the node executes later in a single pass.
    /// </summary>
    public required int SourceOrder { get; init; }

    /// <summary>
    /// Topological order index of the target node.
    /// Lower value means the node executes earlier in a single pass.
    /// </summary>
    public required int TargetOrder { get; init; }

    /// <summary>
    /// How far back this edge jumps in topological order (SourceOrder - TargetOrder).
    /// Used for evaluation priority - larger jumps are evaluated first
    /// to ensure deterministic behavior when multiple back-edges share nodes.
    /// </summary>
    public required int JumpDistance { get; init; }

    // Convenience accessors

    /// <summary>
    /// ID of the source node (where the back-edge originates).
    /// </summary>
    public string SourceNodeId => Edge.From;

    /// <summary>
    /// ID of the target node (where the back-edge points to, triggering re-execution).
    /// </summary>
    public string TargetNodeId => Edge.To;

    /// <summary>
    /// Condition that must be met for this back-edge to trigger re-execution.
    /// Null means unconditional (always triggers if reached).
    /// </summary>
    public EdgeCondition? Condition => Edge.Condition;
}
