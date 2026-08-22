using System.Text.Json.Serialization;

namespace HPD.Graph.Abstractions.Graph;

/// <summary>
/// Directed edge connecting two nodes.
/// Defines execution flow and optional conditional routing.
/// </summary>
public sealed record Edge
{
    /// <summary>
    /// Source node ID.
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// Target node ID.
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// Source output port number (0-indexed).
    /// Null = connect to port 0 (default port).
    /// </summary>
    public int? FromPort { get; init; }

    /// <summary>
    /// Destination input port number (0-indexed).
    /// Reserved for future multi-input support.
    /// Null = connect to default input.
    /// </summary>
    public int? ToPort { get; init; }

    /// <summary>
    /// Priority for edge traversal ordering (lower = higher priority).
    /// Used to ensure deterministic lazy cloning behavior.
    /// Null = use lexicographic ordering by target node ID.
    /// </summary>
    public int? Priority { get; init; }

    /// <summary>
    /// Optional condition for traversing this edge.
    /// If null, edge is always traversed (unconditional).
    /// </summary>
    public EdgeCondition? Condition { get; init; }

    /// <summary>
    /// Runtime-only predicate for traversing this edge.
    /// Not serialized to JSON/config. Use declarative <see cref="Condition"/> for persisted graphs.
    /// When both <see cref="Condition"/> and <see cref="Predicate"/> are set, both must pass.
    /// </summary>
    [JsonIgnore]
    public Func<EdgePredicateContext, bool>? Predicate { get; init; }

    /// <summary>
    /// Override cloning policy for this specific edge.
    /// Null = use graph-level policy.
    /// Use to optimize specific edges (e.g., NeverClone for read-only handlers).
    /// </summary>
    public Execution.CloningPolicy? CloningPolicy { get; init; }

    // ========== PHASE 4: TEMPORAL OPERATORS ==========

    /// <summary>
    /// Delay before traversing this edge (Phase 4: Temporal Operators).
    /// Smart threshold: &lt; 30s = synchronous Task.Delay, ≥ 30s = checkpoint and suspend.
    /// Example: TimeSpan.FromMinutes(5) = wait 5 minutes before traversing edge.
    /// Null = no delay.
    /// </summary>
    public TimeSpan? Delay { get; init; }

    /// <summary>
    /// Retry policy for edge traversal (Phase 4: Temporal Operators).
    /// Polls condition until met or timeout.
    /// OUTER LOOP: Retries edge traversal (before node execution).
    /// Example: Wait for external file, API availability, quota, etc.
    /// Null = no retry policy.
    /// </summary>
    public Execution.EdgeRetryPolicy? RetryPolicy { get; init; }

    /// <summary>
    /// Additional metadata (labels, weights, etc.).
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}
