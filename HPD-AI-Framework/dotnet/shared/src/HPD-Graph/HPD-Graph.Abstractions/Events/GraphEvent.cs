using HPD.Events;

namespace HPDAgent.Graph.Abstractions.Events;

/// <summary>
/// Base class for graph-specific events.
/// Inherits from HPD.Events.Event to participate in unified event streaming.
/// </summary>
public abstract record GraphEvent : Event
{
    /// <summary>
    /// Graph execution context (graph-specific field).
    /// Contains graph ID, execution state, and metadata.
    /// </summary>
    public GraphExecutionContext? GraphContext { get; init; }
}

/// <summary>
/// Graph execution context attached to graph events.
/// Provides graph-level metadata for event consumers.
/// </summary>
public sealed record GraphExecutionContext
{
    /// <summary>
    /// Unique identifier for this graph execution.
    /// </summary>
    public required string GraphId { get; init; }

    /// <summary>
    /// Total number of nodes in the graph.
    /// </summary>
    public int TotalNodes { get; init; }

    /// <summary>
    /// Number of nodes completed so far.
    /// </summary>
    public int CompletedNodes { get; init; }

    /// <summary>
    /// Current execution layer index (null if not layer-based).
    /// </summary>
    public int? CurrentLayer { get; init; }

    /// <summary>
    /// Additional metadata for cross-domain scenarios.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}
