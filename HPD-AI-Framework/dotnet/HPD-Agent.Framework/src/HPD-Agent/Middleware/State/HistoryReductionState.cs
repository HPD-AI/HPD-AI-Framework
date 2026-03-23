namespace HPD.Agent;

/// <summary>
/// State for history reduction middleware. Immutable record with static abstract key.
/// Caches history reduction results to avoid expensive re-summarization on every turn.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>
/// This state is immutable and flows through the context.
/// It is NOT stored in middleware instance fields, preserving thread safety for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Caching Strategy:</b></para>
/// <list type="bullet">
/// <item>Cache key: Message hash (first N messages)</item>
/// <item>Cache validity: Based on count + threshold (unit determined by CountingUnit)</item>
/// <item>Cache hit: Reuse summary, no LLM call</item>
/// <item>Cache miss: Re-summarize, update cache</item>
/// </list>
/// </remarks>
[MiddlewareState(Persistent = true)]
public sealed record HistoryReductionStateData
{
    /// <summary>
    /// Last successful reduction (null if no reduction performed yet).
    /// Contains the summary, metadata, and validity information.
    /// </summary>
    public CachedReduction? LastReduction { get; init; }

    /// <summary>
    /// Number of completed exchanges (RunAsync calls) on this branch.
    /// Incremented by AfterMessageTurnAsync after each successful exchange.
    /// Used by HistoryReductionMiddleware when CountingUnit == Exchanges.
    /// </summary>
    public int ExchangeCount { get; init; }

    /// <summary>
    /// Records a new reduction, replacing the previous cache entry.
    /// </summary>
    public HistoryReductionStateData WithReduction(CachedReduction reduction) =>
        this with { LastReduction = reduction };

    /// <summary>
    /// Increments the exchange counter by one.
    /// </summary>
    public HistoryReductionStateData WithIncrementedExchangeCount() =>
        this with { ExchangeCount = ExchangeCount + 1 };
}

/// <summary>
/// Represents a cached history reduction result.
/// Contains summary, metadata, and integrity checking for cache validation.
/// </summary>
/// <remarks>
/// <para><b>Design Rationale:</b></para>
/// <para>
/// This record captures everything needed to reuse a reduction across multiple turns:
/// - Summary content (to inject in place of old messages)
/// - Metadata for cache validation (count, hash, threshold, counting unit)
/// - Integrity checking (to detect message modifications)
/// </para>
///
/// <para><b>Cache Validity:</b></para>
/// <code>
/// ┌────────────────────────────────────────────────────────┐
/// │ Turn 1: 20 exchanges → Reduce → Summary               │
/// │         Cache: CountAtReduction=20                     │
/// │                SummarizedUpToIndex=N (message index)  │
/// │                ReductionThreshold=5                    │
/// └────────────────────────────────────────────────────────┘
///                          ↓
///              IsValidFor(currentCount, unit)?
///                          ↓
/// ┌────────────────────────────────────────────────────────┐
/// │ Turn 2: 24 exchanges                                   │
/// │         24 - 20 = 4 new exchanges                      │
/// │         4 &lt;= 5 (threshold) → CACHE HIT               │
/// └────────────────────────────────────────────────────────┘
///                          ↓
/// ┌────────────────────────────────────────────────────────┐
/// │ Turn 3: 26 exchanges                                   │
/// │         26 - 20 = 6 new exchanges                      │
/// │         6 &gt; 5 (threshold) → CACHE MISS                  │
/// │         Re-summarize required                          │
/// └────────────────────────────────────────────────────────┘
/// </code>
/// </remarks>
public sealed record CachedReduction
{
    /// <summary>
    /// Message index where summary ends (exclusive).
    /// Messages [0..SummarizedUpToIndex) were summarized.
    /// Messages [SummarizedUpToIndex..end) are kept verbatim.
    /// Always a message index regardless of CountingUnit.
    /// </summary>
    public int SummarizedUpToIndex { get; init; }

    /// <summary>
    /// Count value (in the unit specified by CountingUnit) when this reduction was performed.
    /// Used to detect how many new exchanges/messages have been added since reduction.
    /// </summary>
    public int CountAtReduction { get; init; }

    /// <summary>
    /// Generated summary text (replaces messages 0..SummarizedUpToIndex).
    /// </summary>
    public string SummaryContent { get; init; } = string.Empty;

    /// <summary>
    /// When this reduction was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Hash of summarized messages (for integrity checking).
    /// Detects if messages were modified/deleted/reordered.
    /// </summary>
    public string MessageHash { get; init; } = string.Empty;

    /// <summary>
    /// Target count from configuration (for reference). Unit determined by CountingUnit.
    /// </summary>
    public int TargetCount { get; init; }

    /// <summary>
    /// Threshold for triggering re-reduction.
    /// Number of new units (exchanges or messages) allowed before cache expires.
    /// </summary>
    public int ReductionThreshold { get; init; }

    /// <summary>
    /// The counting unit that was active when this reduction was created.
    /// Cache is invalid if the unit changes.
    /// </summary>
    public HistoryCountingUnit CountingUnit { get; init; }

    /// <summary>
    /// Checks if this reduction is still valid for the given count and unit.
    /// Returns true if new units are within threshold and unit hasn't changed.
    /// </summary>
    public bool IsValidFor(int currentCount, HistoryCountingUnit currentUnit)
    {
        if (currentUnit != CountingUnit) return false;
        if (currentCount < CountAtReduction) return false;
        return (currentCount - CountAtReduction) <= ReductionThreshold;
    }

    /// <summary>
    /// Validates that the summarized portion of messages hasn't been modified.
    /// Compares hash of first N messages against stored hash.
    /// </summary>
    public bool ValidateIntegrity(IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        if (messages.Count < SummarizedUpToIndex)
            throw new ArgumentException(
                $"Message count ({messages.Count}) is less than SummarizedUpToIndex ({SummarizedUpToIndex}). " +
                "This indicates messages were deleted, which invalidates the cache.");

        if (SummarizedUpToIndex == 0)
            return true;

        var currentHash = ComputeMessageHash(messages.Take(SummarizedUpToIndex));
        return currentHash == MessageHash;
    }

    /// <summary>
    /// Applies this reduction to a message list, returning reduced messages.
    /// Replaces summarized messages with summary, keeps recent messages verbatim.
    /// </summary>
    public IEnumerable<Microsoft.Extensions.AI.ChatMessage> ApplyToMessages(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> allMessages,
        Microsoft.Extensions.AI.ChatMessage? systemMessage = null)
    {
        var messagesList = allMessages.ToList();

        if (!ValidateIntegrity(messagesList))
        {
            throw new InvalidOperationException(
                "Cannot apply reduction: Message integrity check failed. " +
                "Messages have been modified since reduction was created.");
        }

        var result = new List<Microsoft.Extensions.AI.ChatMessage>();

        if (systemMessage != null)
            result.Add(systemMessage);

        result.Add(new Microsoft.Extensions.AI.ChatMessage(
            Microsoft.Extensions.AI.ChatRole.Assistant,
            SummaryContent));

        result.AddRange(messagesList.Skip(SummarizedUpToIndex));

        return result;
    }

    /// <summary>
    /// Creates a new CachedReduction from reduction results.
    /// </summary>
    public static CachedReduction Create(
        IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> messages,
        string summaryContent,
        int summarizedUpToIndex,
        int targetCount,
        int reductionThreshold,
        int countAtReduction,
        HistoryCountingUnit countingUnit)
    {
        var messageHash = ComputeMessageHash(messages.Take(summarizedUpToIndex));

        return new CachedReduction
        {
            SummarizedUpToIndex = summarizedUpToIndex,
            CountAtReduction = countAtReduction,
            SummaryContent = summaryContent,
            CreatedAt = DateTime.UtcNow,
            MessageHash = messageHash,
            TargetCount = targetCount,
            ReductionThreshold = reductionThreshold,
            CountingUnit = countingUnit
        };
    }

    /// <summary>
    /// Computes a deterministic hash of messages for integrity checking.
    /// Uses Microsoft's AIJsonUtilities.HashDataToString for proper content normalization.
    /// </summary>
    private static string ComputeMessageHash(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages)
    {
        return Microsoft.Extensions.AI.AIJsonUtilities.HashDataToString(
            messages.ToArray(),
            Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions);
    }
}
