using System.Collections.Immutable;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Thread-scoped state for compaction middleware.
/// </summary>
[MiddlewareState(Persistent = true)]
public sealed record CompactionStateData
{
    /// <summary>
    /// Number of completed user-visible message turns on this thread.
    /// </summary>
    public int MessageTurnCount { get; init; }

    /// <summary>
    /// Last turn-level token usage observed after a completed message turn.
    /// </summary>
    public UsageDetails? LastTurnUsage { get; init; }

    /// <summary>
    /// Last per-iteration usage observed after a completed message turn.
    /// </summary>
    public ImmutableList<UsageDetails?> LastIterationUsage { get; init; }
        = ImmutableList<UsageDetails?>.Empty;

    /// <summary>
    /// Last time provider usage was observed by the middleware.
    /// </summary>
    public DateTimeOffset? LastUsageObservedAt { get; init; }

    /// <summary>
    /// Last time a compaction was applied to model-visible history.
    /// </summary>
    public DateTimeOffset? LastAppliedAt { get; init; }

    public CompactionStateData ResetAfterCompaction() =>
        this with
        {
            MessageTurnCount = 0,
            LastTurnUsage = null,
            LastIterationUsage = ImmutableList<UsageDetails?>.Empty,
            LastUsageObservedAt = null,
            LastAppliedAt = DateTimeOffset.UtcNow
        };

    public CompactionStateData WithIncrementedMessageTurnCount() =>
        this with { MessageTurnCount = MessageTurnCount + 1 };

    public CompactionStateData WithObservedUsage(
        UsageDetails? turnUsage,
        ImmutableList<UsageDetails?> iterationUsage) =>
        this with
        {
            LastTurnUsage = turnUsage,
            LastIterationUsage = iterationUsage,
            LastUsageObservedAt = DateTimeOffset.UtcNow
        };
}
