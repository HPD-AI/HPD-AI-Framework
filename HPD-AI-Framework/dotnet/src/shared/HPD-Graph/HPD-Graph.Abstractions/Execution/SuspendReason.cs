namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Why a node was suspended.
/// Universal reasons applicable to ANY workflow.
/// </summary>
public enum SuspendReason
{
    /// <summary>
    /// Human-in-the-loop approval required.
    /// Waits indefinitely until manual resume.
    /// Example: Manual review before deploying to production.
    /// </summary>
    HumanApproval,

    /// <summary>
    /// Polling for condition (sensor pattern).
    /// Reschedules periodically until condition met or timeout.
    /// Requires RetryAfter to be set.
    /// Example: File sensor waiting for input file to appear.
    /// </summary>
    PollingCondition,

    /// <summary>
    /// Waiting for external task completion.
    /// Example: External API callback, webhook, third-party job completion.
    /// </summary>
    ExternalTaskWait,

    /// <summary>
    /// Waiting for resource availability.
    /// Example: GPU slot, database connection, memory quota.
    /// </summary>
    ResourceWait,

    /// <summary>
    /// Edge-level schedule constraint (Phase 4: Temporal Operators).
    /// Waits until scheduled time window before allowing edge traversal.
    /// Example: Edge with Schedule = daily at 3am.
    /// </summary>
    Schedule,

    /// <summary>
    /// Edge-level retry policy (Phase 4: Temporal Operators).
    /// Polls edge condition until met or timeout.
    /// Example: Edge waiting for external dependency before allowing traversal.
    /// </summary>
    EdgeRetry
}
