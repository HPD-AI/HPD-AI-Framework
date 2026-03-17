namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Runtime execution state of a node.
/// Tracked in context tags for observability.
/// Universal states applicable to ANY workflow.
/// </summary>
public enum NodeState
{
    /// <summary>
    /// Node has not started yet (dependencies not met).
    /// Initial state before execution begins.
    /// </summary>
    Pending,

    /// <summary>
    /// Node is currently executing.
    /// Handler code is running.
    /// </summary>
    Running,

    /// <summary>
    /// Node is polling/waiting to retry (sensor pattern).
    /// Example: File sensor checking for file existence every 30 seconds.
    /// </summary>
    Polling,

    /// <summary>
    /// Node suspended for external input (HITL).
    /// Example: Waiting for human approval or external webhook.
    /// </summary>
    Suspended,

    /// <summary>
    /// Node completed successfully.
    /// Terminal state - execution complete.
    /// </summary>
    Succeeded,

    /// <summary>
    /// Node failed.
    /// Terminal state - execution complete with error.
    /// </summary>
    Failed,

    /// <summary>
    /// Node skipped due to condition or upstream failure.
    /// Terminal state - execution did not run.
    /// </summary>
    Skipped,

    /// <summary>
    /// Node cancelled.
    /// Terminal state - execution interrupted.
    /// </summary>
    Cancelled
}

/// <summary>
/// Extension methods for NodeState.
/// </summary>
public static class NodeStateExtensions
{
    /// <summary>
    /// Check if this state is terminal (execution complete).
    /// </summary>
    public static bool IsTerminal(this NodeState state) => state switch
    {
        NodeState.Succeeded => true,
        NodeState.Failed => true,
        NodeState.Skipped => true,
        NodeState.Cancelled => true,
        _ => false
    };

    /// <summary>
    /// Check if this state is active (currently executing or waiting).
    /// </summary>
    public static bool IsActive(this NodeState state) => state switch
    {
        NodeState.Running => true,
        NodeState.Polling => true,
        NodeState.Suspended => true,
        _ => false
    };

    /// <summary>
    /// Check if this state is waiting (polling or suspended).
    /// </summary>
    public static bool IsWaiting(this NodeState state) => state switch
    {
        NodeState.Polling => true,
        NodeState.Suspended => true,
        _ => false
    };
}
