namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Metadata about node execution (universal tracing/correlation).
/// </summary>
public sealed record NodeExecutionMetadata
{
    /// <summary>
    /// Retry attempt number (0 = first try, 1 = first retry, etc.).
    /// </summary>
    public int AttemptNumber { get; init; }

    /// <summary>
    /// Why this node executed.
    /// Example: "Scheduled execution", "Fallback from node X", "Manual retry"
    /// </summary>
    public string? ExecutionReason { get; init; }

    /// <summary>
    /// Unique identifier for this specific execution.
    /// Generated fresh for each node execution.
    /// </summary>
    public string ExecutionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Correlation ID that persists across related executions.
    /// Example: Same correlation across all polling attempts for a sensor.
    /// Used as suspend token in V5 sensor polling.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Execution IDs of upstream nodes that triggered this execution.
    /// Enables tracing message lineage through the graph.
    /// Limited to 100 entries (FIFO eviction in orchestrator).
    /// </summary>
    public IReadOnlyList<string>? ParentExecutionIds { get; init; }

    /// <summary>
    /// When this execution started.
    /// </summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>
    /// Custom metrics and metadata collected during execution.
    /// Primarily numeric metrics (tokens_used, api_calls, cache_hits), but supports objects for structured data.
    /// Use namespaced keys to prevent collisions (v5:*, nodered:*).
    /// Example: { "v5:poll_attempts": 5, "nodered:routing_decision": 0, "tokens_used": 1234 }
    /// </summary>
    public IReadOnlyDictionary<string, object>? CustomMetrics { get; init; }
}
