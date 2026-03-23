namespace HPDAgent.Graph.Abstractions.Orchestration;

/// <summary>
/// Represents a layer in topological execution order.
/// All nodes in the same layer can execute in parallel.
/// </summary>
public sealed record ExecutionLayer
{
    /// <summary>
    /// Layer index (0-based, 0 = first layer after START).
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// Node IDs that can execute in parallel at this layer.
    /// </summary>
    public required IReadOnlyList<string> NodeIds { get; init; }
}
