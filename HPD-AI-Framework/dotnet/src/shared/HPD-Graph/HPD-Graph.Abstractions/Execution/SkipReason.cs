namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Why a node was skipped.
/// Universal reasons applicable to ANY workflow.
/// </summary>
public enum SkipReason
{
    /// <summary>
    /// Upstream dependency failed.
    /// Example: Can't embed chunks if chunking failed.
    /// </summary>
    DependencyFailed,

    /// <summary>
    /// Conditional edge evaluated to false.
    /// Example: Query classification → only run vector search, skip full-text.
    /// </summary>
    ConditionNotMet,

    /// <summary>
    /// Node already completed (resumed from checkpoint).
    /// Idempotency check passed.
    /// </summary>
    AlreadyCompleted,

    /// <summary>
    /// Optional node exceeded its deadline.
    /// Example: Background enrichment timed out, continue without it.
    /// </summary>
    OptionalAndTimedOut,

    /// <summary>
    /// Retry policy exhausted all attempts.
    /// Escalated from Failure to Skipped.
    /// </summary>
    MaxAttemptsExceeded,

    /// <summary>
    /// User explicitly skipped this node.
    /// Example: Manual intervention, debugging.
    /// </summary>
    ManualSkip
}
