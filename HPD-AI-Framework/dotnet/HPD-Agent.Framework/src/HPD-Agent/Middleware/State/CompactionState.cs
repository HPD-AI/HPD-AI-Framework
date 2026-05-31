using System.Collections.Immutable;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Branch-scoped state for compaction middleware.
/// </summary>
[MiddlewareState(Persistent = true)]
public sealed record CompactionStateData
{
    /// <summary>
    /// Last successful compaction snapshot, if any.
    /// </summary>
    public CompactionSnapshot? LastCompaction { get; init; }

    /// <summary>
    /// Number of completed user-visible message turns on this branch.
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

    public CompactionStateData WithCompaction(CompactionSnapshot compaction) =>
        this with
        {
            LastCompaction = compaction,
            LastAppliedAt = DateTimeOffset.UtcNow
        };

    public CompactionStateData WithCompactionApplied(DateTimeOffset appliedAt) =>
        this with { LastAppliedAt = appliedAt };

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

/// <summary>
/// Durable metadata describing the last normalized compaction.
/// </summary>
public sealed record CompactionSnapshot
{
    public IReadOnlyList<string> OriginalMessageIds { get; init; } = [];
    public IReadOnlyList<string> ModelVisibleMessageIds { get; init; } = [];
    public IReadOnlyList<string> ModelCompactedMessageIds { get; init; } = [];
    public IReadOnlyList<string> RetainedMessageIds { get; init; } = [];
    public IReadOnlyList<string> ReplacementMessageIds { get; init; } = [];
    public string? SummaryContent { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static CompactionSnapshot FromResult(CompactionResult result) =>
        new()
        {
            OriginalMessageIds = GetMessageIds(result.OriginalMessages),
            ModelVisibleMessageIds = GetMessageIds(result.ModelVisibleMessages),
            ModelCompactedMessageIds = GetMessageIds(result.ModelCompactedMessages),
            RetainedMessageIds = GetMessageIds(result.RetainedMessages),
            ReplacementMessageIds = GetMessageIds(result.ReplacementMessages),
            SummaryContent = result.SummaryContent,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static IReadOnlyList<string> GetMessageIds(IEnumerable<ChatMessage> messages) =>
        messages
            .Select(message => message.MessageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();
}
