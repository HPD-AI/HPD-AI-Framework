using HPD.Agent;
using HPD.Events;

namespace HPD.MultiAgent;

/// <summary>
/// Event emitted when a multi-agent workflow starts execution.
/// Wraps the internal graph execution start event in agent-idiomatic form.
/// </summary>
public sealed record WorkflowStartedEvent : AgentEvent
{
    /// <summary>
    /// Name of the workflow being executed.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// Number of agent nodes in the workflow.
    /// </summary>
    public required int NodeCount { get; init; }

    /// <summary>
    /// Number of execution layers (null if not layer-based).
    /// </summary>
    public int? LayerCount { get; init; }

    /// <summary>
    /// Override Kind to Lifecycle.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when a multi-agent workflow completes execution.
/// </summary>
public sealed record WorkflowCompletedEvent : AgentEvent
{
    /// <summary>
    /// Name of the workflow that completed.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// Total execution duration.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of agent nodes that completed successfully.
    /// </summary>
    public int SuccessfulNodes { get; init; }

    /// <summary>
    /// Number of agent nodes that failed.
    /// </summary>
    public int FailedNodes { get; init; }

    /// <summary>
    /// Number of agent nodes that were skipped.
    /// </summary>
    public int SkippedNodes { get; init; }

    /// <summary>
    /// Whether the workflow completed successfully (no failed nodes).
    /// </summary>
    public bool Success => FailedNodes == 0;

    /// <summary>
    /// Override Kind to Lifecycle.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when an agent node in a workflow starts execution.
/// </summary>
public sealed record WorkflowNodeStartedEvent : AgentEvent
{
    /// <summary>
    /// Name of the parent workflow.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// ID of the agent node that started.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Name of the agent being executed at this node.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>
    /// Layer index (null for nodes outside layer-based execution).
    /// </summary>
    public int? LayerIndex { get; init; }

    /// <summary>
    /// Override Kind to Lifecycle.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when an agent node in a workflow completes execution.
/// </summary>
public sealed record WorkflowNodeCompletedEvent : AgentEvent
{
    /// <summary>
    /// Name of the parent workflow.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// ID of the agent node that completed.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Name of the agent that executed at this node.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>
    /// Whether the node completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Time taken to execute the node.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Progress percentage (0.0 to 1.0) based on completed nodes / total nodes.
    /// </summary>
    public float Progress { get; init; }

    /// <summary>
    /// Outputs from the completed node (if available).
    /// </summary>
    public IReadOnlyDictionary<string, object>? Outputs { get; init; }

    /// <summary>
    /// Error message if the node failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Override Kind to Lifecycle.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when an agent node is skipped in a workflow.
/// </summary>
public sealed record WorkflowNodeSkippedEvent : AgentEvent
{
    /// <summary>
    /// Name of the parent workflow.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// ID of the node that was skipped.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// Reason for skipping.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Override Kind to Diagnostic.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Diagnostic;
}

/// <summary>
/// Event emitted when an edge is traversed in a workflow (routing between nodes).
/// Useful for debugging workflow routing decisions.
/// </summary>
public sealed record WorkflowEdgeTraversedEvent : AgentEvent
{
    /// <summary>
    /// Name of the parent workflow.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// ID of the source node.
    /// </summary>
    public required string FromNodeId { get; init; }

    /// <summary>
    /// ID of the target node.
    /// </summary>
    public required string ToNodeId { get; init; }

    /// <summary>
    /// Whether this edge had a condition that was evaluated.
    /// </summary>
    public bool HasCondition { get; init; }

    /// <summary>
    /// Description of the condition (if any).
    /// </summary>
    public string? ConditionDescription { get; init; }

    /// <summary>
    /// Override Kind to Diagnostic.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Diagnostic;
}

/// <summary>
/// Event emitted when a workflow layer starts executing (for parallel execution).
/// </summary>
public sealed record WorkflowLayerStartedEvent : AgentEvent
{
    /// <summary>
    /// Name of the parent workflow.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// Layer index (0-based).
    /// </summary>
    public required int LayerIndex { get; init; }

    /// <summary>
    /// Number of nodes in this layer.
    /// </summary>
    public required int NodeCount { get; init; }

    /// <summary>
    /// Override Kind to Lifecycle.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Event emitted when a workflow layer completes executing.
/// </summary>
public sealed record WorkflowLayerCompletedEvent : AgentEvent
{
    /// <summary>
    /// Name of the parent workflow.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// Layer index (0-based).
    /// </summary>
    public required int LayerIndex { get; init; }

    /// <summary>
    /// Time taken to execute this layer.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of nodes successfully completed in this layer.
    /// </summary>
    public int SuccessfulNodes { get; init; }

    /// <summary>
    /// Override Kind to Lifecycle.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Diagnostic event emitted during workflow execution.
/// Wraps internal graph diagnostic events.
/// </summary>
public sealed record WorkflowDiagnosticEvent : AgentEvent
{
    /// <summary>
    /// Name of the parent workflow.
    /// </summary>
    public required string WorkflowName { get; init; }

    /// <summary>
    /// Log level for filtering.
    /// </summary>
    public required LogLevel Level { get; init; }

    /// <summary>
    /// Source of the diagnostic (e.g., node name, "Orchestrator").
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// The diagnostic message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Node ID if this diagnostic is related to a specific node.
    /// </summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// Override Kind to Diagnostic.
    /// </summary>
    public new EventKind Kind { get; init; } = EventKind.Diagnostic;
}

/// <summary>
/// Log level for workflow diagnostic events.
/// Matches Microsoft.Extensions.Logging.LogLevel for consistency.
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}
