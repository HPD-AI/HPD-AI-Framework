using HPD.Events;

namespace HPD.RAG.Core.Events;

/// <summary>
/// Emitted when an evaluation backfill run begins.
/// </summary>
public sealed record EvalStartedEvent : MragEvent
{
    /// <summary>Number of (scenario, iteration) partitions that will be evaluated.</summary>
    public required int ScenarioCount { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when a single (scenario, iteration) partition evaluation completes.
/// </summary>
public sealed record EvalPartitionCompletedEvent : MragEvent
{
    /// <summary>Name of the scenario (first partition key segment).</summary>
    public required string ScenarioName { get; init; }

    /// <summary>Name of the iteration (second partition key segment).</summary>
    public required string IterationName { get; init; }

    /// <summary>
    /// Per-metric scores produced for this partition.
    /// Keys are metric names (e.g. "Relevance", "Groundedness"); values are [0, 1].
    /// </summary>
    public required IReadOnlyDictionary<string, double> Scores { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}

/// <summary>
/// Emitted when all partitions in an evaluation backfill run have been processed.
/// </summary>
public sealed record EvalCompletedEvent : MragEvent
{
    /// <summary>
    /// Macro-averaged scores across all successfully evaluated partitions.
    /// Keys are metric names; values are averages over all partitions.
    /// </summary>
    public required IReadOnlyDictionary<string, double> AverageScores { get; init; }

    /// <summary>Number of partitions that failed to evaluate.</summary>
    public required int FailedCount { get; init; }

    /// <inheritdoc cref="HPD.Events.Event.Kind"/>
    public new EventKind Kind { get; init; } = EventKind.Lifecycle;
}
