using HPDAgent.Graph.Abstractions.Context;

namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Edge-level retry policy for polling/sensor patterns (Phase 4: Temporal Operators).
/// Allows edges to wait for external conditions before traversal.
/// OUTER LOOP: Retries edge traversal (before node execution).
/// Complements node-level retry which is INNER LOOP (during node execution).
/// </summary>
public sealed record EdgeRetryPolicy
{
    /// <summary>
    /// Condition function that must return true for edge to be traversed.
    /// Example: Check if external file exists, API is available, quota is available, etc.
    /// Null = always retry (unconditional polling, requires RetryInterval).
    /// </summary>
    public Func<IGraphContext, Task<bool>>? RetryCondition { get; init; }

    /// <summary>
    /// Interval between retry attempts.
    /// Example: TimeSpan.FromSeconds(10) = check every 10 seconds
    /// </summary>
    public required TimeSpan RetryInterval { get; init; }

    /// <summary>
    /// Maximum time to wait before giving up.
    /// Null = wait indefinitely.
    /// Example: TimeSpan.FromHours(1) = fail after 1 hour if condition never met
    /// Default: 24 hours (if not specified)
    /// </summary>
    public TimeSpan? MaxWaitTime { get; init; }

    /// <summary>
    /// Maximum number of retry attempts.
    /// Null = unlimited retries (until MaxWaitTime).
    /// Example: 10 = try up to 10 times
    /// </summary>
    public int? MaxRetries { get; init; }

    /// <summary>
    /// What to do when max retries or max wait time is exceeded.
    /// - FailGraph: Throw exception and fail the entire graph
    /// - SkipNode: Skip the target node and continue execution
    /// Default: FailGraph
    /// </summary>
    public EdgeRetryExhaustedBehavior ExhaustedBehavior { get; init; } = EdgeRetryExhaustedBehavior.FailGraph;
}

/// <summary>
/// Behavior when edge retry policy is exhausted (max retries or max wait time exceeded).
/// </summary>
public enum EdgeRetryExhaustedBehavior
{
    /// <summary>
    /// Throw exception and fail the entire graph execution.
    /// Use when the condition is critical for correctness.
    /// </summary>
    FailGraph,

    /// <summary>
    /// Skip the target node and continue graph execution.
    /// Use when the edge is optional or has fallback paths.
    /// </summary>
    SkipNode
}
