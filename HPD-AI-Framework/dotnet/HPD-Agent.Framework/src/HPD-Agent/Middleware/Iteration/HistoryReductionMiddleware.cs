using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Reduces conversation history to manage context window size.
/// Uses caching to avoid expensive re-summarization on every turn.
/// </summary>
/// <remarks>
/// <para><b>STATELESS MIDDLEWARE:</b></para>
/// <para>
/// This middleware is stateless - all state flows through the context via
/// <see cref="HistoryReductionStateData"/>. This preserves Agent's thread-safety
/// guarantee for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Lifecycle:</b></para>
/// <para>
/// This middleware uses the <see cref="IAgentMiddleware.BeforeMessageTurnAsync"/> hook
/// which runs BEFORE each LLM call. It modifies <see cref="BeforeMessageTurnContext.ConversationHistory"/>
/// to inject the summary and remove old messages.
/// AfterMessageTurnAsync increments the exchange counter used when CountingUnit == Exchanges.
/// </para>
///
/// <para><b>Caching Strategy:</b></para>
/// <list type="number">
/// <item>Check if cached reduction exists and is valid (IsValidFor)</item>
/// <item>If cache hit: Apply cached summary to messages (fast path)</item>
/// <item>If cache miss: Run reducer (LLM call), cache result, apply to messages</item>
/// <item>Store reduction in state for next iteration</item>
/// </list>
///
/// <para><b>Configuration:</b></para>
/// <para>
/// Configured via <see cref="HistoryReductionConfig"/>:
/// - Strategy: MessageCounting or Summarizing
/// - CountingUnit: Exchanges (default) or Messages
/// - TargetCount: How many exchanges/messages to keep
/// - SummarizationThreshold: How many new units before re-reduction
/// - SummarizerProvider: Optional separate model for summarization
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Auto-registered when HistoryReductionConfig.Enabled = true
/// var agent = new AgentBuilder()
///     .WithHistoryReduction(new HistoryReductionConfig
///     {
///         Enabled = true,
///         Strategy = HistoryReductionStrategy.Summarizing,
///         CountingUnit = HistoryCountingUnit.Exchanges,
///         TargetCount = 20,
///         SummarizationThreshold = 5
///     })
///     .Build();
/// </code>
/// </example>
public class HistoryReductionMiddleware : IAgentMiddleware
{
    //
    // CONFIGURATION (not state - set at registration time)
    //

    /// <summary>
    /// Chat reducer to use for history reduction.
    /// Can be MessageCountingChatReducer or SummarizingChatReducer.
    /// </summary>
    public required IChatReducer? ChatReducer { get; init; }

    /// <summary>
    /// Configuration for history reduction behavior.
    /// </summary>
    public required HistoryReductionConfig Config { get; init; }

    /// <summary>
    /// System instructions to preserve when reducing.
    /// Extracted from SystemMessage or ChatOptions.Instructions.
    /// </summary>
    public string? SystemInstructions { get; init; }

    //
    // HOOKS
    //

    /// <summary>
    /// Called BEFORE each message turn.
    /// Checks thresholds and performs history reduction when needed.
    /// </summary>
    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;

        // Check for explicit skip flag in RunConfig (highest priority)
        if (context.RunConfig.SkipHistoryReduction)
        {
            EmitHistoryReductionEvent(context, HistoryReductionStatus.Skipped,
                reason: "Explicitly skipped via RunConfig.SkipHistoryReduction", startTime: startTime);
            return;
        }

        // Skip if no messages or reduction disabled
        if (context.ConversationHistory == null || context.ConversationHistory.Count == 0)
        {
            EmitHistoryReductionEvent(context, HistoryReductionStatus.Skipped,
                reason: "No messages present", startTime: startTime);
            return;
        }

        // Check for explicit trigger flag in RunConfig (bypasses threshold checks)
        var shouldTrigger = context.RunConfig.TriggerHistoryReduction;

        // If not explicitly triggered, check automatic thresholds
        if (!shouldTrigger)
        {
            var hrState = context.GetMiddlewareState<HistoryReductionStateData>();

            var currentCount = Config.CountingUnit switch
            {
                HistoryCountingUnit.Exchanges => hrState?.ExchangeCount ?? 0,
                HistoryCountingUnit.Messages  => context.ConversationHistory.Count,
                _                             => hrState?.ExchangeCount ?? 0
            };

            var threshold = Config.SummarizationThreshold ?? 5;

            if (currentCount <= Config.TargetCount + threshold)
            {
                EmitHistoryReductionEvent(context, HistoryReductionStatus.Skipped,
                    reason: "Count below threshold", startTime: startTime,
                    originalMessageCount: context.ConversationHistory.Count);
                return;
            }
        }

        // Read state
        var hrStateForReduction = context.GetMiddlewareState<HistoryReductionStateData>();

        // Separate system messages from conversation messages
        var systemMessages = context.ConversationHistory.Where(m => m.Role == ChatRole.System).ToList();
        var conversationMessages = context.ConversationHistory.Where(m => m.Role != ChatRole.System).ToList();

        // Determine current count for cache validity check
        var currentCountForCache = Config.CountingUnit switch
        {
            HistoryCountingUnit.Exchanges => hrStateForReduction?.ExchangeCount ?? 0,
            HistoryCountingUnit.Messages  => conversationMessages.Count,
            _                             => hrStateForReduction?.ExchangeCount ?? 0
        };

        // Try cache first
        CachedReduction? activeReduction = null;

        if (hrStateForReduction?.LastReduction != null &&
            hrStateForReduction.LastReduction.IsValidFor(currentCountForCache, Config.CountingUnit))
        {
            //  CACHE HIT: Reuse existing reduction
            activeReduction = hrStateForReduction.LastReduction;

            var reducedMessages = activeReduction.ApplyToMessages(conversationMessages, systemMessage: null).ToList();

            context.ConversationHistory.Clear();
            foreach (var msg in systemMessages.Concat(reducedMessages))
                context.ConversationHistory.Add(msg);

            EmitHistoryReductionEvent(context, HistoryReductionStatus.CacheHit,
                startTime: startTime,
                originalMessageCount: conversationMessages.Count,
                reducedMessageCount: reducedMessages.Count,
                summaryContent: activeReduction.SummaryContent,
                cacheAge: DateTime.UtcNow - activeReduction.CreatedAt);

            return;
        }

        //  CACHE MISS: Need to perform reduction
        if (ChatReducer != null && ShouldTriggerReduction(conversationMessages))
        {
            var reduced = await ChatReducer.ReduceAsync(conversationMessages, cancellationToken).ConfigureAwait(false);

            if (reduced != null)
            {
                var reducedList = reduced.ToList();

                var summaryMsg = reducedList.FirstOrDefault(m => m.Role == ChatRole.Assistant);

                if (summaryMsg != null)
                {
                    int removedCount = conversationMessages.Count - reducedList.Count + 1;
                    var summarizedUpToIndex = removedCount;

                    activeReduction = CachedReduction.Create(
                        conversationMessages,
                        summaryMsg.Text ?? string.Empty,
                        summarizedUpToIndex,
                        Config.TargetCount,
                        Config.SummarizationThreshold ?? 5,
                        countAtReduction: currentCountForCache,
                        countingUnit: Config.CountingUnit);

                    context.ConversationHistory.Clear();
                    foreach (var msg in systemMessages.Concat(reducedList))
                        context.ConversationHistory.Add(msg);

                    context.UpdateMiddlewareState<HistoryReductionStateData>(s =>
                        s.WithReduction(activeReduction)
                    );

                    EmitHistoryReductionEvent(context, HistoryReductionStatus.Performed,
                        startTime: startTime,
                        originalMessageCount: conversationMessages.Count,
                        reducedMessageCount: reducedList.Count,
                        messagesRemoved: removedCount,
                        summaryContent: activeReduction.SummaryContent);

                    TerminateIfCircuitBreaker(context, "History reduction performed - circuit breaker triggered");

                    return;
                }
            }
        }
        else
        {
            EmitHistoryReductionEvent(context, HistoryReductionStatus.Skipped,
                reason: "Reduction threshold not met", startTime: startTime,
                originalMessageCount: conversationMessages.Count);
        }
    }

    /// <summary>
    /// Called AFTER each message turn completes.
    /// Increments the exchange counter used when CountingUnit == Exchanges.
    /// </summary>
    public Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        context.UpdateMiddlewareState<HistoryReductionStateData>(s =>
            s.WithIncrementedExchangeCount()
        );

        return Task.CompletedTask;
    }

    //
    // HELPERS
    //

    /// <summary>
    /// Determines if history reduction should be triggered based on raw message count.
    /// This check always uses message count since it operates on the actual message list.
    /// </summary>
    private bool ShouldTriggerReduction(List<ChatMessage> messages)
    {
        var targetCount = Config.TargetCount;
        var threshold = Config.SummarizationThreshold ?? 5;
        var triggerCount = targetCount + threshold;

        return messages.Count > triggerCount;
    }

    /// <summary>
    /// Emits a unified history reduction event for all scenarios.
    /// </summary>
    private void EmitHistoryReductionEvent(
        BeforeMessageTurnContext context,
        HistoryReductionStatus status,
        DateTimeOffset startTime,
        int? originalMessageCount = null,
        int? reducedMessageCount = null,
        int? messagesRemoved = null,
        string? summaryContent = null,
        TimeSpan? cacheAge = null,
        string? reason = null)
    {
        try
        {
            var duration = DateTimeOffset.UtcNow - startTime;

            context.Emit(new HistoryReductionEvent(
                AgentName: context.AgentName,
                Iteration: 0,
                Status: status,
                Strategy: Config.Strategy,
                OriginalMessageCount: originalMessageCount,
                ReducedMessageCount: reducedMessageCount,
                MessagesRemoved: messagesRemoved,
                SummaryContent: summaryContent,
                SummaryLength: summaryContent?.Length,
                CacheAge: cacheAge,
                Duration: duration,
                Reason: reason,
                Timestamp: DateTimeOffset.UtcNow));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional
        }
    }

    /// <summary>
    /// Checks if circuit breaker mode is enabled and terminates the agent if so.
    /// </summary>
    private void TerminateIfCircuitBreaker(BeforeMessageTurnContext context, string reason)
    {
        var effectiveBehavior = context.RunConfig.HistoryReductionBehaviorOverride ?? Config.Behavior;

        if (effectiveBehavior == HistoryReductionBehavior.CircuitBreaker)
        {
            context.UpdateState(s => s with
            {
                IsTerminated = true,
                TerminationReason = reason
            });
        }
    }
}

//
// OBSERVABILITY EVENTS
//

/// <summary>
/// Status of history reduction operation.
/// </summary>
public enum HistoryReductionStatus
{
    /// <summary>Reduction was skipped (no messages, below threshold, etc.)</summary>
    Skipped,

    /// <summary>Cached reduction was reused (no LLM call needed)</summary>
    CacheHit,

    /// <summary>New reduction was performed (LLM call made to summarize)</summary>
    Performed
}

/// <summary>
/// Unified event emitted for all history reduction scenarios.
/// </summary>
public sealed record HistoryReductionEvent(
    string AgentName,
    int Iteration,
    HistoryReductionStatus Status,
    HistoryReductionStrategy Strategy,
    int? OriginalMessageCount,
    int? ReducedMessageCount,
    int? MessagesRemoved,
    string? SummaryContent,
    int? SummaryLength,
    TimeSpan? CacheAge,
    TimeSpan Duration,
    string? Reason,
    DateTimeOffset Timestamp) : AgentEvent, IObservabilityEvent;
