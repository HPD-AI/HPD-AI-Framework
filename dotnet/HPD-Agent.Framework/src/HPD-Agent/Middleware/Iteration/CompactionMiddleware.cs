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

    public Func<CompactionStrategyOptions, AgentRunConfig, ICompactionStrategy?>? StrategyFactory { get; init; }

    public string? SystemInstructions { get; init; }

    public IThreadCompactionPlanner ThreadCompactionPlanner { get; init; } = new ThreadCompactionPlanner();

    public IThreadHistoryCompactor ThreadHistoryCompactor { get; init; } = new ThreadHistoryCompactor();

    internal async Task<bool> CompactExplicitAsync(
        BeforeMessageTurnContext context,
        ThreadCompactionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedAt = DateTimeOffset.UtcNow;
        var strategyOptions = request.Strategy ?? Config.Strategy;
        var retention = request.Retention ?? Config.Retention;
        var conversation = context.ThreadHistory.Where(message => message.Role != ChatRole.System).ToList();
        if (conversation.Count == 0)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped, startedAt,
                reason: "No messages present", strategyOptions: strategyOptions,
                continuation: request.Continuation, origin: CompactionOrigin.Explicit).ConfigureAwait(false);
            return false;
        }

        var strategy = ResolveStrategy(strategyOptions, context.RunConfig)
            ?? throw new InvalidOperationException("No compaction strategy is configured.");
        await EmitCompactionEventAsync(context, CompactionStatus.Started, startedAt,
            originalMessageCount: conversation.Count,
            reason: "Explicit thread compaction requested",
            strategyOptions: strategyOptions,
            continuation: request.Continuation, origin: CompactionOrigin.Explicit).ConfigureAwait(false);

        var result = await strategy.ReduceAsync(conversation, cancellationToken).ConfigureAwait(false);
        if (result.ModelCompactedMessages.Count == 0)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped, startedAt,
                originalMessageCount: conversation.Count,
                compactedMessageCount: result.ModelVisibleMessages.Count,
                messagesRemoved: 0,
                reason: GetNothingToCompactReason(strategyOptions),
                strategyOptions: strategyOptions,
                continuation: request.Continuation, origin: CompactionOrigin.Explicit).ConfigureAwait(false);
            return false;
        }

        var policy = new EffectiveCompactionPolicy(
            request.Continuation,
            strategyOptions,
            new CountCompactionTriggerOptions(),
            retention,
            request.ModelContext);
        await ApplyThreadCompactionAsync(context, result, policy, cancellationToken).ConfigureAwait(false);

        var system = context.ThreadHistory.Where(message => message.Role == ChatRole.System).ToList();
        context.ThreadHistory.Clear();
        context.ThreadHistory.AddRange(system);
        context.ThreadHistory.AddRange(result.ModelVisibleMessages);
        ResetCompactionState(context);

        await EmitCompactionEventAsync(context, CompactionStatus.Performed, startedAt,
            originalMessageCount: conversation.Count,
            compactedMessageCount: result.ModelVisibleMessages.Count,
            messagesRemoved: result.ModelCompactedMessages.Count,
            summaryContent: result.SummaryContent,
            reason: "Explicit thread compaction completed",
            strategyOptions: strategyOptions,
            continuation: request.Continuation, origin: CompactionOrigin.Explicit).ConfigureAwait(false);
        return true;
    }

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        var policy = EffectiveCompactionPolicy.ResolveAutomatic(Config, context.RunConfig);
        if (policy is null)
            return;

        if (context.ThreadHistory == null || context.ThreadHistory.Count == 0)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                reason: "No messages present", startTime: startTime,
                strategyOptions: policy.Strategy,
                continuation: policy.Continuation);
            return;
        }

        var hrState = context.GetMiddlewareState<CompactionStateData>();
        var decision = GetTriggerDecision(context, hrState, policy);
        if (!decision.ShouldReduce)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                reason: decision.Description ?? "Compaction threshold not met",
                startTime: startTime,
                originalMessageCount: context.ThreadHistory.Count,
                strategyOptions: policy.Strategy,
                continuation: policy.Continuation);
            return;
        }

        var strategy = ResolveStrategy(policy.Strategy, context.RunConfig);
        if (strategy == null)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                reason: "No compaction strategy is configured", startTime: startTime,
                strategyOptions: policy.Strategy,
                continuation: policy.Continuation);
            return;
        }

        var systemMessages = context.ThreadHistory.Where(m => m.Role == ChatRole.System).ToList();
        var conversationMessages = context.ThreadHistory.Where(m => m.Role != ChatRole.System).ToList();

        await EmitCompactionEventAsync(context, CompactionStatus.Started,
            reason: decision.Description ?? "Compaction started",
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            strategyOptions: policy.Strategy,
            continuation: policy.Continuation);

        var result = await strategy.ReduceAsync(conversationMessages, cancellationToken).ConfigureAwait(false);

        // Generated handoff wrappers are not a substantive compaction on their own.
        // A reducer that removes no original messages must be reported as a no-op even
        // when the memento layer adds a context-boundary replacement message.
        if (result.ModelCompactedMessages.Count == 0)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                reason: GetNothingToCompactReason(policy.Strategy),
                startTime: startTime,
                originalMessageCount: conversationMessages.Count,
                compactedMessageCount: result.ModelVisibleMessages.Count,
                messagesRemoved: 0,
                strategyOptions: policy.Strategy,
                continuation: policy.Continuation);
            return;
        }

        await ApplyThreadCompactionAsync(context, result, policy, cancellationToken).ConfigureAwait(false);
        context.ThreadHistory.Clear();
        foreach (var message in systemMessages.Concat(result.ModelVisibleMessages))
            context.ThreadHistory.Add(message);
        ResetCompactionState(context);

        await EmitCompactionEventAsync(context, CompactionStatus.Performed,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: result.ModelVisibleMessages.Count,
            messagesRemoved: result.ModelCompactedMessages.Count,
            summaryContent: result.SummaryContent,
            reason: decision.Description,
            strategyOptions: policy.Strategy,
            continuation: policy.Continuation);

        TerminateIfRequested(
            context,
            policy.Continuation,
            policy.Continuation == CompactionContinuation.StopAfterCompaction
                ? "History compaction performed - stopping before model turn"
                : "History compaction performed - circuit breaker triggered");
    }

    /// <summary>
    /// Re-evaluates automatic compaction at the final model-input boundary.
    /// Tool calls and results can grow context substantially after the message-turn hook,
    /// so every model iteration must be able to reuse or advance the compacted projection.
    /// </summary>
    public async Task BeforeIterationAsync(
        BeforeIterationContext context,
        CancellationToken cancellationToken)
    {
        var policy = EffectiveCompactionPolicy.ResolveAutomatic(Config, context.RunConfig);
        if (policy is null || context.Messages.Count == 0)
            return;

        var startTime = DateTimeOffset.UtcNow;
        var hrState = context.GetMiddlewareState<CompactionStateData>();
        var lastIterationInputTokens = context.PreviousIterationUsage
            .LastOrDefault(usage => usage?.InputTokenCount is not null)
            ?.InputTokenCount;
        var decision = GetTriggerDecision(
            policy.Trigger,
            context.Messages,
            hrState?.MessageTurnCount ?? 0,
            lastIterationInputTokens ?? hrState?.LastTurnUsage?.InputTokenCount,
            policy.ModelContext);

        if (!decision.ShouldReduce)
            return;

        var strategy = ResolveStrategy(policy.Strategy, context.RunConfig);
        if (strategy == null)
            return;

        var systemMessages = context.Messages.Where(message => message.Role == ChatRole.System).ToList();
        var conversationMessages = context.Messages.Where(message => message.Role != ChatRole.System).ToList();

        await EmitCompactionEventAsync(context, CompactionStatus.Started,
            reason: decision.Description ?? "Iteration compaction started",
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            strategyOptions: policy.Strategy,
            continuation: policy.Continuation);

        var result = await strategy.ReduceAsync(conversationMessages, cancellationToken).ConfigureAwait(false);
        if (result.ModelCompactedMessages.Count == 0)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                reason: GetNothingToCompactReason(policy.Strategy),
                startTime: startTime,
                originalMessageCount: conversationMessages.Count,
                compactedMessageCount: result.ModelVisibleMessages.Count,
                messagesRemoved: 0,
                strategyOptions: policy.Strategy,
                continuation: policy.Continuation);
            return;
        }

        await ApplyThreadCompactionAsync(context, result, policy, cancellationToken).ConfigureAwait(false);
        context.Messages.Clear();
        context.Messages.AddRange(systemMessages);
        context.Messages.AddRange(result.ModelVisibleMessages);
        ResetCompactionState(context);

        await EmitCompactionEventAsync(context, CompactionStatus.Performed,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: result.ModelVisibleMessages.Count,
            messagesRemoved: result.ModelCompactedMessages.Count,
            summaryContent: result.SummaryContent,
            reason: decision.Description,
            strategyOptions: policy.Strategy,
            continuation: policy.Continuation);
    }

    private async Task ApplyThreadCompactionAsync(
        HookContext context,
        CompactionResult result,
        EffectiveCompactionPolicy policy,
        CancellationToken cancellationToken)
    {
        if (context.Thread is null)
            return;

        var plan = ThreadCompactionPlanner.Plan(context.Thread, result, policy.Retention);
        if (plan is null)
            return;

        var threadCompaction = ThreadHistoryCompactor.Prepare(context.Thread, plan);
        await context.PublishAsync(threadCompaction.CheckpointEvent, cancellationToken);
        ThreadHistoryCompactor.ApplyCommitted(context.Thread, threadCompaction);
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

    public async Task BeforeThreadForkCommitAsync(
        BeforeThreadForkCommitContext context,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        var forkCompaction = ResolveForkCompaction(context.ForkOptions.Compaction);

        if (forkCompaction.Mode == ThreadForkCompactionMode.Disabled)
            return;

        if (context.TargetThread.Messages.Count == 0)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                reason: "No messages present on fork target", startTime: startTime,
                origin: CompactionOrigin.Fork);
            return;
        }

        var strategyOptions = forkCompaction.Strategy ?? Config.Strategy;
        var strategy = ResolveStrategy(strategyOptions, new AgentRunConfig());
        if (strategy == null)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                reason: "No compaction strategy is configured", startTime: startTime,
                strategyOptions: strategyOptions, origin: CompactionOrigin.Fork);
            return;
        }

        var systemMessages = context.TargetThread.Messages
            .Where(message => message.Role == ChatRole.System)
            .ToList();
        var conversationMessages = context.TargetThread.Messages
            .Where(message => message.Role != ChatRole.System)
            .ToList();

        if (conversationMessages.Count == 0)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                reason: "No non-system messages present on fork target", startTime: startTime,
                strategyOptions: strategyOptions, origin: CompactionOrigin.Fork);
            return;
        }

        await EmitCompactionEventAsync(context, CompactionStatus.Started,
            reason: "Compacting fork target before thread commit",
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            strategyOptions: strategyOptions,
            origin: CompactionOrigin.Fork);

        var result = await strategy.ReduceAsync(conversationMessages, cancellationToken).ConfigureAwait(false);
        if (result.ModelCompactedMessages.Count == 0)
        {
            await EmitCompactionEventAsync(context, CompactionStatus.Skipped,
                startTime: startTime,
                originalMessageCount: conversationMessages.Count,
                reason: GetNothingToCompactReason(strategyOptions),
                strategyOptions: strategyOptions,
                origin: CompactionOrigin.Fork).ConfigureAwait(false);
            return;
        }

        context.TargetThread.Messages.Clear();
        foreach (var message in systemMessages.Concat(result.ModelVisibleMessages))
            context.TargetThread.Messages.Add(message);

        ResetCompactionState(context);

        await EmitCompactionEventAsync(context, CompactionStatus.Performed,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: result.ModelVisibleMessages.Count,
            messagesRemoved: result.ModelCompactedMessages.Count,
            summaryContent: result.SummaryContent,
            reason: "Compacted fork target before thread commit",
            strategyOptions: strategyOptions,
            origin: CompactionOrigin.Fork);
    }

    private ThreadForkCompactionOptions ResolveForkCompaction(
        ThreadForkCompactionOptions? requested)
    {
        var policy = requested ?? ThreadForkCompactionOptions.Inherit;
        if (policy.Mode != ThreadForkCompactionMode.Inherit)
            return policy;

        var configured = Config.ForkCompaction ?? ThreadForkCompactionOptions.Disabled;
        return configured.Mode switch
        {
            ThreadForkCompactionMode.Inherit => configured with { Mode = ThreadForkCompactionMode.Disabled },
            _ => configured
        };
    }

    private ICompactionStrategy? ResolveStrategy(
        CompactionStrategyOptions options,
        AgentRunConfig runConfig)
    {
        if (options == Config.Strategy)
            return Strategy;

        return StrategyFactory?.Invoke(options, runConfig);
    }

    private static string GetNothingToCompactReason(CompactionStrategyOptions strategy)
    {
        if (!string.IsNullOrWhiteSpace(strategy.PreserveFromMessageTurnId) ||
            !string.IsNullOrWhiteSpace(strategy.PreserveFromMessageId))
        {
            return "Nothing exists before the selected message boundary to compact.";
        }

        var preservedTurns = strategy switch
        {
            MessageCountingCompactionOptions counting => counting.PreserveRecentUserTurnCount,
            SummarizingCompactionOptions summarizing => summarizing.PreserveRecentUserTurnCount,
            _ => 0
        };

        return preservedTurns > 0
            ? $"Nothing to compact because the strategy preserves the {preservedTurns} most recent user " +
              (preservedTurns == 1 ? "turn." : "turns.")
            : "Nothing to compact.";
    }

    private CompactionTriggerDecision GetTriggerDecision(
        BeforeMessageTurnContext context,
        CompactionStateData? state,
        EffectiveCompactionPolicy policy)
        => GetTriggerDecision(
            policy.Trigger,
            context.ThreadHistory,
            state?.MessageTurnCount ?? 0,
            state?.LastTurnUsage?.InputTokenCount,
            policy.ModelContext);

    private static CompactionTriggerDecision GetTriggerDecision(
        CompactionTriggerOptions trigger,
        IReadOnlyList<ChatMessage> messages,
        int messageTurnCount,
        long? lastInputTokens,
        ModelContextWindowOptions? modelContext)
    {
        return trigger switch
        {
            CountCompactionTriggerOptions countTrigger => GetCountTriggerDecision(
                countTrigger,
                messages,
                messageTurnCount,
                lastInputTokens),
            ContextWindowCompactionTriggerOptions contextTrigger => GetContextWindowTriggerDecision(
                contextTrigger,
                lastInputTokens,
                modelContext),
            CompositeCompactionTriggerOptions compositeTrigger => GetCompositeTriggerDecision(
                compositeTrigger,
                messages,
                messageTurnCount,
                lastInputTokens,
                modelContext),
            _ => new CompactionTriggerDecision(false, CompactionTriggerReason.None, null, null, "Unknown trigger policy")
        };
    }

    private static CompactionTriggerDecision GetCountTriggerDecision(
        CountCompactionTriggerOptions trigger,
        IReadOnlyList<ChatMessage> messages,
        int messageTurnCount,
        long? lastInputTokens)
    {
        var currentCount = trigger.CountingUnit switch
        {
            HistoryCountingUnit.MessageTurns => messageTurnCount,
            HistoryCountingUnit.Messages => messages.Count,
            _ => messageTurnCount
        };

        var triggerCount = trigger.TargetCount + trigger.Threshold;
        var shouldReduce = currentCount > triggerCount;

        return new CompactionTriggerDecision(
            shouldReduce,
            shouldReduce ? CompactionTriggerReason.CountThreshold : CompactionTriggerReason.None,
            currentCount,
            lastInputTokens,
            shouldReduce ? $"Count threshold exceeded ({currentCount} > {triggerCount})" : "Count below threshold");
    }

    private static CompactionTriggerDecision GetContextWindowTriggerDecision(
        ContextWindowCompactionTriggerOptions trigger,
        long? lastInputTokens,
        ModelContextWindowOptions? modelContext)
    {
        var contextWindowSize = trigger.ContextWindowSize
            ?? modelContext?.ContextWindow
            ?? modelContext?.InputTokenLimit;

        long? triggerTokens = trigger.ThresholdMode switch
        {
            ContextWindowCompactionThresholdMode.Percentage when contextWindowSize.HasValue =>
                (long)(contextWindowSize.Value * trigger.TriggerPercentage),
            ContextWindowCompactionThresholdMode.Percentage => null,
            ContextWindowCompactionThresholdMode.TokenCount => trigger.TriggerTokenCount,
            _ => null
        };

        if (!triggerTokens.HasValue)
        {
            var reason = trigger.ThresholdMode == ContextWindowCompactionThresholdMode.Percentage
                ? "Context window trigger skipped because selected model context window is unknown"
                : "Context window trigger skipped because trigger token count is unknown";
            return new CompactionTriggerDecision(
                false,
                CompactionTriggerReason.None,
                null,
                lastInputTokens,
                reason);
        }

        var shouldReduce = lastInputTokens.HasValue && lastInputTokens.Value > triggerTokens.Value;

        return new CompactionTriggerDecision(
            shouldReduce,
            shouldReduce ? CompactionTriggerReason.ContextWindowThreshold : CompactionTriggerReason.None,
            null,
            lastInputTokens,
            shouldReduce ? $"Context window threshold exceeded ({lastInputTokens} > {triggerTokens})" : "Context window threshold not met");
    }

    private static CompactionTriggerDecision GetCompositeTriggerDecision(
        CompositeCompactionTriggerOptions trigger,
        IReadOnlyList<ChatMessage> messages,
        int messageTurnCount,
        long? lastInputTokens,
        ModelContextWindowOptions? modelContext)
    {
        foreach (var child in trigger.AnyOf)
        {
            var decision = GetTriggerDecision(
                child,
                messages,
                messageTurnCount,
                lastInputTokens,
                modelContext);
            if (decision.ShouldReduce)
            {
                return decision with { Reason = CompactionTriggerReason.Composite };
            }
        }

        return new CompactionTriggerDecision(
            false,
            CompactionTriggerReason.None,
            null,
            lastInputTokens,
            "No composite trigger threshold met");
    }

    private async Task EmitCompactionEventAsync(
        HookContext context,
        CompactionStatus status,
        DateTimeOffset startTime,
        int? originalMessageCount = null,
        int? compactedMessageCount = null,
        int? messagesRemoved = null,
        string? summaryContent = null,
        string? reason = null,
        CompactionStrategyOptions? strategyOptions = null,
        CompactionContinuation continuation = CompactionContinuation.Continue,
        CompactionOrigin origin = CompactionOrigin.Automatic)
    {
        try
        {
            var duration = DateTimeOffset.UtcNow - startTime;

            await context.PublishAsync(new CompactionEvent(
                AgentName: context.AgentName,
                Iteration: 0,
                Status: status,
                Strategy: GetStrategyKind(strategyOptions ?? Config.Strategy),
                OriginalMessageCount: originalMessageCount,
                CompactedMessageCount: compactedMessageCount,
                MessagesRemoved: messagesRemoved,
                SummaryContent: summaryContent,
                SummaryLength: summaryContent?.Length,
                Duration: duration,
                Reason: reason,
                Continuation: continuation,
                Origin: origin));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional.
        }
    }

    private static void TerminateIfRequested(
        BeforeMessageTurnContext context,
        CompactionContinuation behavior,
        string reason)
    {
        if (behavior is CompactionContinuation.StopAfterCompaction)
        {
            context.UpdateState(s => s with
            {
                IsTerminated = true,
                TerminationReason = reason
            });
        }
    }

    private static void ResetCompactionState(HookContext context)
    {
        context.UpdateMiddlewareState<CompactionStateData>(state => state.ResetAfterCompaction());
    }

    internal static CompactionStrategy GetStrategyKind(CompactionStrategyOptions strategy) =>
        strategy switch
        {
            MessageCountingCompactionOptions => CompactionStrategy.MessageCounting,
            SummarizingCompactionOptions => CompactionStrategy.Summarizing,
            _ => CompactionStrategy.MessageCounting
    };
}

internal sealed record EffectiveCompactionPolicy(
    CompactionContinuation Continuation,
    CompactionStrategyOptions Strategy,
    CompactionTriggerOptions Trigger,
    CompactionRetentionOptions Retention,
    ModelContextWindowOptions? ModelContext)
{
    public static EffectiveCompactionPolicy? ResolveAutomatic(
        CompactionConfig config,
        AgentRunConfig runConfig)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(runConfig);

        var run = runConfig.Compaction;
        var automatic = run is null ? config.Automatic : run.Automatic;
        if (automatic is null)
            return null;

        return new EffectiveCompactionPolicy(
            automatic.Continuation,
            run?.Strategy ?? config.Strategy,
            automatic.Trigger,
            run?.Retention ?? config.Retention,
            run?.ModelContext);
    }
}

public enum CompactionStatus
{
    Started,
    Skipped,
    Performed
}

public enum CompactionOrigin
{
    Automatic,
    Explicit,
    Fork
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
    TimeSpan Duration,
    string? Reason,
    CompactionContinuation Continuation = CompactionContinuation.Continue,
    CompactionOrigin Origin = CompactionOrigin.Automatic) : AgentEvent, IObservabilityEvent;
