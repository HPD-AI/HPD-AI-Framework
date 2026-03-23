namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// Type of graph node.
/// </summary>
public enum NodeType
{
    /// <summary>
    /// Entry point of the graph.
    /// </summary>
    Start,

    /// <summary>
    /// Exit point of the graph.
    /// </summary>
    End,

    /// <summary>
    /// Regular handler node (executes IGraphNodeHandler).
    /// </summary>
    Handler,

    /// <summary>
    /// Router node for conditional branching.
    /// </summary>
    Router,

    /// <summary>
    /// Nested graph execution (modular composition).
    /// </summary>
    SubGraph,

    /// <summary>
    /// Parallel iteration over collections.
    /// Executes a processor graph once per item in an input collection.
    /// Similar to SubGraph but with iteration semantics and concurrency control.
    /// </summary>
    Map
}
