namespace HPDAgent.Graph.Abstractions.State;

/// <summary>
/// Managed context - graph-provided execution metadata.
/// Contains execution info that's not user state.
/// NOT checkpointed (ephemeral).
/// </summary>
public interface IManagedContext
{
    /// <summary>
    /// Total node executions so far in this graph execution.
    /// </summary>
    int CurrentStep { get; }

    /// <summary>
    /// Estimated total steps (if known).
    /// Null if estimation is not available.
    /// </summary>
    int? EstimatedTotalSteps { get; }

    /// <summary>
    /// Whether this is the last node in the graph.
    /// </summary>
    bool IsLastNode { get; }

    /// <summary>
    /// Time elapsed since graph execution started.
    /// </summary>
    TimeSpan ElapsedTime { get; }

    /// <summary>
    /// Estimated remaining time (if estimation is available).
    /// Null if estimation is not available.
    /// </summary>
    TimeSpan? RemainingTime { get; }

    /// <summary>
    /// Execution metrics (custom key-value pairs).
    /// Example: token_count, cost, api_calls, etc.
    /// NOT checkpointed (ephemeral).
    /// </summary>
    IReadOnlyDictionary<string, double> Metrics { get; }

    /// <summary>
    /// Record a metric value (overwrites existing).
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Metric value</param>
    void RecordMetric(string name, double value);

    /// <summary>
    /// Increment a metric by a delta (or initialize to delta if not exists).
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="delta">Amount to increment (default: 1.0)</param>
    void IncrementMetric(string name, double delta = 1.0);
}
