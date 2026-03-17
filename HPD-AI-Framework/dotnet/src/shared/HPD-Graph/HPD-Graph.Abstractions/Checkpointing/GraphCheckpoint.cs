using HPDAgent.Graph.Abstractions.Execution;

namespace HPDAgent.Graph.Abstractions.Checkpointing;

/// <summary>
/// Immutable snapshot of graph execution state.
/// Enables resume-from-failure and incremental execution.
/// </summary>
public sealed record GraphCheckpoint
{
    /// <summary>
    /// Unique identifier for this checkpoint.
    /// </summary>
    public required string CheckpointId { get; init; }

    /// <summary>
    /// The execution this checkpoint belongs to.
    /// </summary>
    public required string ExecutionId { get; init; }

    /// <summary>
    /// The graph being executed.
    /// </summary>
    public required string GraphId { get; init; }

    /// <summary>
    /// When this checkpoint was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Nodes that have successfully completed.
    /// Used to skip already-completed nodes on resume.
    /// </summary>
    public required IReadOnlySet<string> CompletedNodes { get; init; }

    /// <summary>
    /// Named outputs from completed nodes.
    /// Format: "nodeId.outputName" → value
    /// Used to reconstruct handler inputs on resume.
    /// </summary>
    public required IReadOnlyDictionary<string, object> NodeOutputs { get; init; }

    /// <summary>
    /// Full execution context (serialized).
    /// Includes channels, managed context, logs, etc.
    /// </summary>
    public required string ContextJson { get; init; }

    /// <summary>
    /// Node state metadata for version-aware resume.
    /// Maps node ID to versioned state metadata.
    /// Used to validate compatibility during checkpoint resume.
    /// </summary>
    public IReadOnlyDictionary<string, NodeStateMetadata> NodeStateMetadata { get; init; } =
        new Dictionary<string, NodeStateMetadata>();

    /// <summary>
    /// Optional metadata about this checkpoint.
    /// </summary>
    public CheckpointMetadata? Metadata { get; init; }

    /// <summary>
    /// Schema version for forward/backward compatibility.
    /// </summary>
    public string SchemaVersion { get; init; } = "1.0";

    // ===== ITERATION STATE (Cyclic Graphs) =====

    /// <summary>
    /// Current iteration index at checkpoint time (0-based).
    /// 0 for acyclic graphs or first iteration.
    /// </summary>
    public int CurrentIteration { get; init; } = 0;

    /// <summary>
    /// Nodes pending re-execution when checkpoint was taken.
    /// Empty for acyclic graphs or when iteration completed cleanly.
    /// Used for resuming mid-iteration.
    /// </summary>
    public IReadOnlySet<string> PendingDirtyNodes { get; init; } = new HashSet<string>();
}

/// <summary>
/// Additional metadata about a checkpoint.
/// </summary>
public sealed record CheckpointMetadata
{
    /// <summary>
    /// What triggered this checkpoint.
    /// </summary>
    public required CheckpointTrigger Trigger { get; init; }

    /// <summary>
    /// The node that just completed (if Trigger = NodeCompleted).
    /// </summary>
    public string? CompletedNodeId { get; init; }

    /// <summary>
    /// The layer that just completed (if Trigger = LayerCompleted).
    /// </summary>
    public int? CompletedLayer { get; init; }

    /// <summary>
    /// Custom metadata (e.g., cost tracking, performance metrics).
    /// </summary>
    public IReadOnlyDictionary<string, object>? CustomMetadata { get; init; }

    // ===== ITERATION TRACKING (Cyclic Graphs) =====

    /// <summary>
    /// Iteration index when checkpoint was created (for cyclic graphs).
    /// Null for acyclic graphs or non-iteration checkpoints.
    /// </summary>
    public int? IterationIndex { get; init; }

    /// <summary>
    /// True if checkpoint was taken mid-iteration (between back-edge evaluation).
    /// </summary>
    public bool IsMidIteration =>
        IterationIndex.HasValue && Trigger == CheckpointTrigger.Suspension;

    // ===== SUSPENSION TRACKING (Layered Suspension) =====

    /// <summary>
    /// ID of the node that triggered suspension (if Trigger = Suspension).
    /// </summary>
    public string? SuspendedNodeId { get; init; }

    /// <summary>
    /// Token for matching suspend/resume operations.
    /// Used to correlate checkpoint with approval response.
    /// </summary>
    public string? SuspendToken { get; init; }

    /// <summary>
    /// Current outcome of the suspension.
    /// Pending = waiting for response.
    /// Approved/Denied/TimedOut = terminal states.
    /// </summary>
    public SuspensionOutcome? SuspensionOutcome { get; init; }
}

/// <summary>
/// What triggered a checkpoint to be saved.
/// </summary>
public enum CheckpointTrigger
{
    /// <summary>
    /// After a single node completed.
    /// Most granular, highest storage cost.
    /// </summary>
    NodeCompleted,

    /// <summary>
    /// After an entire layer completed.
    /// RECOMMENDED for most use cases.
    /// </summary>
    LayerCompleted,

    /// <summary>
    /// Manually triggered by user code.
    /// </summary>
    Manual,

    /// <summary>
    /// Triggered during graceful shutdown.
    /// </summary>
    Shutdown,

    /// <summary>
    /// Triggered when a node suspends for human-in-the-loop approval.
    /// Checkpoint saved before waiting for response.
    /// </summary>
    Suspension,

    /// <summary>
    /// Triggered after an iteration completes in cyclic graph execution.
    /// Includes iteration state for potential resume.
    /// </summary>
    IterationCompleted,

    /// <summary>
    /// Triggered when max iterations is reached with dirty nodes remaining.
    /// Graph did not converge within iteration limit.
    /// </summary>
    MaxIterationsReached
}
