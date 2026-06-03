using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>
/// Reduces model-visible conversation history according to the configured strategy, trigger, and retention policy.
/// </summary>
public class CompactionMiddleware : IAgentMiddleware
{
    public required ICompactionStrategy? Strategy { get; init; }

    public required CompactionConfig Config { get; init; }

    public string? SystemInstructions { get; init; }

    public IBranchCompactionPlanner BranchCompactionPlanner { get; init; } = new BranchCompactionPlanner();

    public IBranchHistoryCompactor BranchHistoryCompactor { get; init; } = new BranchHistoryCompactor();

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;

        if (context.RunConfig.SkipCompaction)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "Explicitly skipped via RunConfig.SkipCompaction", startTime: startTime);
            return;
        }

        if (context.BranchHistory == null || context.BranchHistory.Count == 0)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No messages present", startTime: startTime);
            return;
        }

        if (Strategy == null)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No compaction strategy is configured", startTime: startTime);
            return;
        }

        var hrState = context.GetMiddlewareState<CompactionStateData>();
        var decision = GetTriggerDecision(context, hrState);
        if (!decision.ShouldReduce)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: decision.Description ?? "Compaction threshold not met",
                startTime: startTime,
                originalMessageCount: context.BranchHistory.Count);
            return;
        }

        var systemMessages = context.BranchHistory.Where(m => m.Role == ChatRole.System).ToList();
        var conversationMessages = context.BranchHistory.Where(m => m.Role != ChatRole.System).ToList();

        var result = await Strategy.ReduceAsync(conversationMessages, cancellationToken).ConfigureAwait(false);

        context.BranchHistory.Clear();
        foreach (var message in systemMessages.Concat(result.ModelVisibleMessages))
            context.BranchHistory.Add(message);

        var snapshot = CompactionSnapshot.FromResult(result);
        context.UpdateMiddlewareState<CompactionStateData>(state =>
            state.WithCompaction(snapshot));

        if (context.Branch is not null)
        {
            var plan = BranchCompactionPlanner.Plan(context.Branch, result, Config.Retention);
            if (plan is not null)
                await BranchHistoryCompactor.CompactAsync(context.Branch, plan, cancellationToken).ConfigureAwait(false);
        }

        EmitCompactionEvent(context, CompactionStatus.Performed,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: result.ModelVisibleMessages.Count,
            messagesRemoved: result.ModelCompactedMessages.Count,
            summaryContent: result.SummaryContent,
            reason: decision.Description);

        TerminateIfCircuitBreaker(context, "History compaction performed - circuit breaker triggered");
    }

    public Task AfterMessageTurnAsync(
        AfterMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        context.UpdateMiddlewareState<CompactionStateData>(state =>
            state
                .WithIncrementedMessageTurnCount()
                .WithObservedUsage(context.TurnUsage, context.IterationUsage));

        return Task.CompletedTask;
    }

    public async Task BeforeBranchForkCommitAsync(
        BeforeBranchForkCommitContext context,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;

        if (!ShouldCompactFork(context))
            return;

        if (context.TargetBranch.Messages.Count == 0)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No messages present on fork target", startTime: startTime);
            return;
        }

        if (Strategy == null)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No compaction strategy is configured", startTime: startTime);
            return;
        }

        var systemMessages = context.TargetBranch.Messages
            .Where(message => message.Role == ChatRole.System)
            .ToList();
        var conversationMessages = context.TargetBranch.Messages
            .Where(message => message.Role != ChatRole.System)
            .ToList();

        if (conversationMessages.Count == 0)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No non-system messages present on fork target", startTime: startTime);
            return;
        }

        var result = await Strategy.ReduceAsync(conversationMessages, cancellationToken).ConfigureAwait(false);

        context.TargetBranch.Messages.Clear();
        foreach (var message in systemMessages.Concat(result.ModelVisibleMessages))
            context.TargetBranch.Messages.Add(message);

        var snapshot = CompactionSnapshot.FromResult(result);
        context.UpdateMiddlewareState<CompactionStateData>(state =>
            state.WithCompaction(snapshot));

        EmitCompactionEvent(context, CompactionStatus.Performed,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: result.ModelVisibleMessages.Count,
            messagesRemoved: result.ModelCompactedMessages.Count,
            summaryContent: result.SummaryContent,
            reason: "Compacted fork target before branch commit");
    }

    private bool ShouldCompactFork(BeforeBranchForkCommitContext context) =>
        context.ForkOptions.CompactionIntent switch
        {
            BranchForkCompactionIntent.Enabled => true,
            BranchForkCompactionIntent.Disabled => false,
            _ => Config.CompactOnFork
        };

    private CompactionTriggerDecision GetTriggerDecision(
        BeforeMessageTurnContext context,
        CompactionStateData? state)
    {
        if (context.RunConfig.TriggerCompaction)
        {
            return new CompactionTriggerDecision(
                ShouldReduce: true,
                Reason: CompactionTriggerReason.ExplicitRunTrigger,
                CurrentCount: null,
                LastInputTokens: state?.LastTurnUsage?.InputTokenCount,
                Description: "Explicitly triggered via RunConfig.TriggerCompaction");
        }

        return GetTriggerDecision(Config.Trigger, context, state);
    }

    private static CompactionTriggerDecision GetTriggerDecision(
        CompactionTriggerOptions trigger,
        BeforeMessageTurnContext context,
        CompactionStateData? state)
    {
        return trigger switch
        {
            CountCompactionTriggerOptions countTrigger => GetCountTriggerDecision(countTrigger, context, state),
            TokenBudgetCompactionTriggerOptions tokenTrigger => GetTokenTriggerDecision(tokenTrigger, state),
            ContextWindowCompactionTriggerOptions contextTrigger => GetContextWindowTriggerDecision(contextTrigger, state),
            CompositeCompactionTriggerOptions compositeTrigger => GetCompositeTriggerDecision(compositeTrigger, context, state),
            _ => new CompactionTriggerDecision(false, CompactionTriggerReason.None, null, null, "Unknown trigger policy")
        };
    }

    private static CompactionTriggerDecision GetCountTriggerDecision(
        CountCompactionTriggerOptions trigger,
        BeforeMessageTurnContext context,
        CompactionStateData? state)
    {
        var currentCount = trigger.CountingUnit switch
        {
            HistoryCountingUnit.MessageTurns => state?.MessageTurnCount ?? 0,
            HistoryCountingUnit.Messages => context.BranchHistory?.Count ?? 0,
            _ => state?.MessageTurnCount ?? 0
        };

        var triggerCount = trigger.TargetCount + trigger.Threshold;
        var shouldReduce = currentCount > triggerCount;

        return new CompactionTriggerDecision(
            shouldReduce,
            shouldReduce ? CompactionTriggerReason.CountThreshold : CompactionTriggerReason.None,
            currentCount,
            state?.LastTurnUsage?.InputTokenCount,
            shouldReduce ? $"Count threshold exceeded ({currentCount} > {triggerCount})" : "Count below threshold");
    }

    private static CompactionTriggerDecision GetTokenTriggerDecision(
        TokenBudgetCompactionTriggerOptions trigger,
        CompactionStateData? state)
    {
        var lastInputTokens = state?.LastTurnUsage?.InputTokenCount;
        var triggerTokens = trigger.TargetTokenBudget + trigger.TokenBudgetThreshold;
        var shouldReduce = lastInputTokens.HasValue && lastInputTokens.Value > triggerTokens;

        return new CompactionTriggerDecision(
            shouldReduce,
            shouldReduce ? CompactionTriggerReason.TokenBudgetThreshold : CompactionTriggerReason.None,
            null,
            lastInputTokens,
            shouldReduce ? $"Token budget threshold exceeded ({lastInputTokens} > {triggerTokens})" : "Token budget threshold not met");
    }

    private static CompactionTriggerDecision GetContextWindowTriggerDecision(
        ContextWindowCompactionTriggerOptions trigger,
        CompactionStateData? state)
    {
        var lastInputTokens = state?.LastTurnUsage?.InputTokenCount;
        var triggerTokens = (long)(trigger.ContextWindowSize * trigger.TriggerPercentage);
        var shouldReduce = lastInputTokens.HasValue && lastInputTokens.Value > triggerTokens;

        return new CompactionTriggerDecision(
            shouldReduce,
            shouldReduce ? CompactionTriggerReason.ContextWindowThreshold : CompactionTriggerReason.None,
            null,
            lastInputTokens,
            shouldReduce ? $"Context window threshold exceeded ({lastInputTokens} > {triggerTokens})" : "Context window threshold not met");
    }

    private static CompactionTriggerDecision GetCompositeTriggerDecision(
        CompositeCompactionTriggerOptions trigger,
        BeforeMessageTurnContext context,
        CompactionStateData? state)
    {
        foreach (var child in trigger.AnyOf)
        {
            var decision = GetTriggerDecision(child, context, state);
            if (decision.ShouldReduce)
            {
                return decision with { Reason = CompactionTriggerReason.Composite };
            }
        }

        return new CompactionTriggerDecision(
            false,
            CompactionTriggerReason.None,
            null,
            state?.LastTurnUsage?.InputTokenCount,
            "No composite trigger threshold met");
    }

    private void EmitCompactionEvent(
        HookContext context,
        CompactionStatus status,
        DateTimeOffset startTime,
        int? originalMessageCount = null,
        int? compactedMessageCount = null,
        int? messagesRemoved = null,
        string? summaryContent = null,
        TimeSpan? cacheAge = null,
        string? reason = null)
    {
        try
        {
            var duration = DateTimeOffset.UtcNow - startTime;

            context.Emit(new CompactionEvent(
                AgentName: context.AgentName,
                Iteration: 0,
                Status: status,
                Strategy: GetStrategyKind(Config.Strategy),
                OriginalMessageCount: originalMessageCount,
                CompactedMessageCount: compactedMessageCount,
                MessagesRemoved: messagesRemoved,
                SummaryContent: summaryContent,
                SummaryLength: summaryContent?.Length,
                CacheAge: cacheAge,
                Duration: duration,
                Reason: reason));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional.
        }
    }

    private void TerminateIfCircuitBreaker(BeforeMessageTurnContext context, string reason)
    {
        var effectiveBehavior = context.RunConfig.CompactionBehaviorOverride ?? Config.Behavior;

        if (effectiveBehavior == CompactionBehavior.CircuitBreaker)
        {
            context.UpdateState(s => s with
            {
                IsTerminated = true,
                TerminationReason = reason
            });
        }
    }

    internal static CompactionStrategy GetStrategyKind(CompactionStrategyOptions strategy) =>
        strategy switch
        {
            MessageCountingCompactionOptions => CompactionStrategy.MessageCounting,
            SummarizingCompactionOptions => CompactionStrategy.Summarizing,
            _ => CompactionStrategy.MessageCounting
        };
}

public enum CompactionStatus
{
    Skipped,
    CacheHit,
    Performed
}

public sealed record CompactionEvent(
    string AgentName,
    int Iteration,
    CompactionStatus Status,
    CompactionStrategy Strategy,
    int? OriginalMessageCount,
    int? CompactedMessageCount,
    int? MessagesRemoved,
    string? SummaryContent,
    int? SummaryLength,
    TimeSpan? CacheAge,
    TimeSpan Duration,
    string? Reason) : AgentEvent, IObservabilityEvent;
