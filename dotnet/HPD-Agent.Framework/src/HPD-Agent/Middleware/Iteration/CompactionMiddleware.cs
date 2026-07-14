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

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        var policy = EffectiveCompactionPolicy.Resolve(Config, context.RunConfig);

        if (policy.Mode == CompactionRunMode.Disabled)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "Explicitly disabled via RunConfig.Compaction.Mode", startTime: startTime,
                strategyOptions: policy.Strategy,
                mode: policy.Mode,
                behavior: policy.Behavior);
            return;
        }

        if (context.ThreadHistory == null || context.ThreadHistory.Count == 0)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No messages present", startTime: startTime,
                strategyOptions: policy.Strategy,
                mode: policy.Mode,
                behavior: policy.Behavior);
            TerminateForcedCompactionIfRequested(context, policy);
            return;
        }

        var hrState = context.GetMiddlewareState<CompactionStateData>();
        if (policy.Mode == CompactionRunMode.Auto &&
            TryApplyCachedCompaction(context, hrState?.LastCompaction, startTime, policy))
        {
            return;
        }

        var strategy = ResolveStrategy(policy.Strategy, context.RunConfig);
        if (strategy == null)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No compaction strategy is configured", startTime: startTime,
                strategyOptions: policy.Strategy,
                mode: policy.Mode,
                behavior: policy.Behavior);
            TerminateForcedCompactionIfRequested(context, policy);
            return;
        }

        var decision = GetTriggerDecision(context, hrState, policy);
        if (!decision.ShouldReduce)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: decision.Description ?? "Compaction threshold not met",
                startTime: startTime,
                originalMessageCount: context.ThreadHistory.Count,
                strategyOptions: policy.Strategy,
                mode: policy.Mode,
                behavior: policy.Behavior);
            TerminateForcedCompactionIfRequested(context, policy);
            return;
        }

        var systemMessages = context.ThreadHistory.Where(m => m.Role == ChatRole.System).ToList();
        var conversationMessages = context.ThreadHistory.Where(m => m.Role != ChatRole.System).ToList();

        EmitCompactionEvent(context, CompactionStatus.Started,
            reason: decision.Description ?? "Compaction started",
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            strategyOptions: policy.Strategy,
            mode: policy.Mode,
            behavior: policy.Behavior);

        var result = await strategy.ReduceAsync(conversationMessages, cancellationToken).ConfigureAwait(false);

        // Generated handoff wrappers are not a substantive compaction on their own.
        // A reducer that removes no original messages must be reported as a no-op even
        // when the memento layer adds a context-boundary replacement message.
        if (result.ModelCompactedMessages.Count == 0)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: GetNothingToCompactReason(policy.Strategy),
                startTime: startTime,
                originalMessageCount: conversationMessages.Count,
                compactedMessageCount: result.ModelVisibleMessages.Count,
                messagesRemoved: 0,
                strategyOptions: policy.Strategy,
                mode: policy.Mode,
                behavior: policy.Behavior);
            TerminateForcedCompactionIfRequested(context, policy);
            return;
        }

        context.ThreadHistory.Clear();
        foreach (var message in systemMessages.Concat(result.ModelVisibleMessages))
            context.ThreadHistory.Add(message);

        var snapshot = CompactionSnapshot.FromResult(result);
        context.UpdateMiddlewareState<CompactionStateData>(state =>
            state.WithCompaction(snapshot));

        if (context.Thread is not null)
        {
            var plan = ThreadCompactionPlanner.Plan(context.Thread, result, policy.Retention);
            if (plan is not null)
            {
                var threadCompaction = await ThreadHistoryCompactor
                    .CompactAsync(context.Thread, plan, cancellationToken)
                    .ConfigureAwait(false);
                context.Emit(threadCompaction.CheckpointEvent);
            }
        }

        EmitCompactionEvent(context, CompactionStatus.Performed,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: result.ModelVisibleMessages.Count,
            messagesRemoved: result.ModelCompactedMessages.Count,
            summaryContent: result.SummaryContent,
            reason: decision.Description,
            strategyOptions: policy.Strategy,
            mode: policy.Mode,
            behavior: policy.Behavior);

        TerminateIfRequested(
            context,
            policy.Behavior,
            policy.Behavior == CompactionBehavior.StopAfterCompaction
                ? "History compaction performed - stopping before model turn"
                : "History compaction performed - circuit breaker triggered");
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
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No messages present on fork target", startTime: startTime);
            return;
        }

        var strategyOptions = forkCompaction.Strategy ?? Config.Strategy;
        var strategy = ResolveStrategy(strategyOptions, new AgentRunConfig());
        if (strategy == null)
        {
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No compaction strategy is configured", startTime: startTime,
                strategyOptions: strategyOptions);
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
            EmitCompactionEvent(context, CompactionStatus.Skipped,
                reason: "No non-system messages present on fork target", startTime: startTime,
                strategyOptions: strategyOptions);
            return;
        }

        if (forkCompaction.PreferCache &&
            forkCompaction.Strategy is null &&
            TryApplyCachedForkCompaction(context, conversationMessages, systemMessages, startTime))
        {
            return;
        }

        EmitCompactionEvent(context, CompactionStatus.Started,
            reason: "Compacting fork target before thread commit",
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            strategyOptions: strategyOptions);

        var result = await strategy.ReduceAsync(conversationMessages, cancellationToken).ConfigureAwait(false);

        context.TargetThread.Messages.Clear();
        foreach (var message in systemMessages.Concat(result.ModelVisibleMessages))
            context.TargetThread.Messages.Add(message);

        var snapshot = CompactionSnapshot.FromResult(result);
        context.UpdateMiddlewareState<CompactionStateData>(state =>
            state.WithCompaction(snapshot));

        EmitCompactionEvent(context, CompactionStatus.Performed,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: result.ModelVisibleMessages.Count,
            messagesRemoved: result.ModelCompactedMessages.Count,
            summaryContent: result.SummaryContent,
            reason: "Compacted fork target before thread commit",
            strategyOptions: strategyOptions);
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

    private bool TryApplyCachedForkCompaction(
        BeforeThreadForkCommitContext context,
        IReadOnlyList<ChatMessage> conversationMessages,
        IReadOnlyList<ChatMessage> systemMessages,
        DateTimeOffset startTime)
    {
        var cached = context.GetMiddlewareState<CompactionStateData>()?.LastCompaction;
        if (cached is null)
            return false;

        var currentOriginalIds = GetMessageIds(conversationMessages);
        if (!currentOriginalIds.SequenceEqual(cached.OriginalMessageIds, StringComparer.Ordinal))
            return false;

        var modelVisibleMessages = CloneMessages(cached.ModelVisibleMessages);
        if (modelVisibleMessages.Count == 0)
            return false;

        context.TargetThread.Messages.Clear();
        foreach (var message in systemMessages.Concat(modelVisibleMessages))
            context.TargetThread.Messages.Add(message);

        context.UpdateMiddlewareState<CompactionStateData>(state =>
            state.WithCompactionApplied(DateTimeOffset.UtcNow));

        EmitCompactionEvent(context, CompactionStatus.CacheHit,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: modelVisibleMessages.Count,
            messagesRemoved: cached.ModelCompactedMessageIds.Count,
            summaryContent: cached.SummaryContent,
            cacheAge: DateTimeOffset.UtcNow - cached.CreatedAt,
            reason: "Reused cached fork compaction result");

        return true;
    }

    private bool TryApplyCachedCompaction(
        BeforeMessageTurnContext context,
        CompactionSnapshot? cached,
        DateTimeOffset startTime,
        EffectiveCompactionPolicy policy)
    {
        if (cached is null || cached.ModelVisibleMessages.Count == 0)
            return false;

        var systemMessages = context.ThreadHistory.Where(m => m.Role == ChatRole.System).ToList();
        var conversationMessages = context.ThreadHistory.Where(m => m.Role != ChatRole.System).ToList();
        if (!TryGetMessagesAfterCachedOriginalPrefix(conversationMessages, cached, out var newMessages))
            return false;

        var modelVisibleMessages = CloneMessages(cached.ModelVisibleMessages);
        context.ThreadHistory.Clear();
        foreach (var message in systemMessages.Concat(modelVisibleMessages).Concat(newMessages))
            context.ThreadHistory.Add(message);

        context.UpdateMiddlewareState<CompactionStateData>(state =>
            state.WithCompactionApplied(DateTimeOffset.UtcNow));

        EmitCompactionEvent(context, CompactionStatus.CacheHit,
            startTime: startTime,
            originalMessageCount: conversationMessages.Count,
            compactedMessageCount: modelVisibleMessages.Count + newMessages.Count,
            messagesRemoved: cached.ModelCompactedMessageIds.Count,
            summaryContent: cached.SummaryContent,
            cacheAge: DateTimeOffset.UtcNow - cached.CreatedAt,
            reason: "Reused cached compacted context",
            strategyOptions: policy.Strategy,
            mode: policy.Mode,
            behavior: policy.Behavior);

        return true;
    }

    private static bool TryGetMessagesAfterCachedOriginalPrefix(
        IReadOnlyList<ChatMessage> conversationMessages,
        CompactionSnapshot cached,
        out IReadOnlyList<ChatMessage> newMessages)
    {
        newMessages = [];
        if (cached.OriginalMessageIds.Count == 0 ||
            conversationMessages.Count < cached.OriginalMessageIds.Count)
        {
            return false;
        }

        for (var i = 0; i < cached.OriginalMessageIds.Count; i++)
        {
            if (!string.Equals(
                    conversationMessages[i].MessageId,
                    cached.OriginalMessageIds[i],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        newMessages = conversationMessages
            .Skip(cached.OriginalMessageIds.Count)
            .Select(CloneMessage)
            .ToList();
        return true;
    }

    private static IReadOnlyList<ChatMessage> CloneMessages(IEnumerable<ChatMessage> messages) =>
        messages.Select(CloneMessage).ToList();

    private static ChatMessage CloneMessage(ChatMessage message) =>
        new(message.Role, message.Contents.ToArray())
        {
            MessageId = message.MessageId,
            AuthorName = message.AuthorName,
            CreatedAt = message.CreatedAt,
            RawRepresentation = message.RawRepresentation,
            AdditionalProperties = message.AdditionalProperties is null
                ? null
                : new AdditionalPropertiesDictionary(message.AdditionalProperties)
        };

    private static IReadOnlyList<string> GetMessageIds(IEnumerable<ChatMessage> messages) =>
        messages
            .Select(message => message.MessageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

    private ICompactionStrategy? ResolveStrategy(
        CompactionStrategyOptions options,
        AgentRunConfig runConfig)
    {
        if (runConfig.Compaction?.Mode == CompactionRunMode.Force &&
            options is SummarizingCompactionOptions summarizing &&
            summarizing.ResummarizeAfterNewMessages != 0)
        {
            options = summarizing with { ResummarizeAfterNewMessages = 0 };
        }

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
    {
        if (policy.Mode == CompactionRunMode.Force)
        {
            return new CompactionTriggerDecision(
                ShouldReduce: true,
                Reason: CompactionTriggerReason.ExplicitRunTrigger,
                CurrentCount: null,
                LastInputTokens: state?.LastTurnUsage?.InputTokenCount,
                Description: "Explicitly triggered via RunConfig.Compaction.Mode");
        }

        return GetTriggerDecision(policy.Trigger, context, state, policy.ModelContext);
    }

    private static CompactionTriggerDecision GetTriggerDecision(
        CompactionTriggerOptions trigger,
        BeforeMessageTurnContext context,
        CompactionStateData? state,
        ModelContextWindowOptions? modelContext)
    {
        return trigger switch
        {
            CountCompactionTriggerOptions countTrigger => GetCountTriggerDecision(countTrigger, context, state),
            ContextWindowCompactionTriggerOptions contextTrigger => GetContextWindowTriggerDecision(contextTrigger, state, modelContext),
            CompositeCompactionTriggerOptions compositeTrigger => GetCompositeTriggerDecision(compositeTrigger, context, state, modelContext),
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
            HistoryCountingUnit.Messages => context.ThreadHistory?.Count ?? 0,
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

    private static CompactionTriggerDecision GetContextWindowTriggerDecision(
        ContextWindowCompactionTriggerOptions trigger,
        CompactionStateData? state,
        ModelContextWindowOptions? modelContext)
    {
        var lastInputTokens = state?.LastTurnUsage?.InputTokenCount;
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
        BeforeMessageTurnContext context,
        CompactionStateData? state,
        ModelContextWindowOptions? modelContext)
    {
        foreach (var child in trigger.AnyOf)
        {
            var decision = GetTriggerDecision(child, context, state, modelContext);
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
        string? reason = null,
        CompactionStrategyOptions? strategyOptions = null,
        CompactionRunMode mode = CompactionRunMode.Auto,
        CompactionBehavior behavior = CompactionBehavior.Continue)
    {
        try
        {
            var duration = DateTimeOffset.UtcNow - startTime;

            context.Emit(new CompactionEvent(
                AgentName: context.AgentName,
                Iteration: 0,
                Status: status,
                Strategy: GetStrategyKind(strategyOptions ?? Config.Strategy),
                OriginalMessageCount: originalMessageCount,
                CompactedMessageCount: compactedMessageCount,
                MessagesRemoved: messagesRemoved,
                SummaryContent: summaryContent,
                SummaryLength: summaryContent?.Length,
                CacheAge: cacheAge,
                Duration: duration,
                Reason: reason,
                Mode: mode,
                Behavior: behavior));
        }
        catch (InvalidOperationException)
        {
            // EventCoordinator not configured - event emission is optional.
        }
    }

    private static void TerminateIfRequested(
        BeforeMessageTurnContext context,
        CompactionBehavior behavior,
        string reason)
    {
        if (behavior is CompactionBehavior.StopAfterCompaction)
        {
            context.UpdateState(s => s with
            {
                IsTerminated = true,
                TerminationReason = reason
            });
        }
    }

    private static void TerminateForcedCompactionIfRequested(
        BeforeMessageTurnContext context,
        EffectiveCompactionPolicy policy)
    {
        if (policy.Mode is CompactionRunMode.Force &&
            policy.Behavior is CompactionBehavior.StopAfterCompaction)
        {
            TerminateIfRequested(
                context,
                policy.Behavior,
                "History compaction skipped - stopping before model turn");
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

internal sealed record EffectiveCompactionPolicy(
    CompactionRunMode Mode,
    CompactionBehavior Behavior,
    CompactionStrategyOptions Strategy,
    CompactionTriggerOptions Trigger,
    CompactionRetentionOptions Retention,
    ModelContextWindowOptions? ModelContext)
{
    public static EffectiveCompactionPolicy Resolve(
        CompactionConfig config,
        AgentRunConfig runConfig)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(runConfig);

        var run = runConfig.Compaction;
        return new EffectiveCompactionPolicy(
            run?.Mode ?? CompactionRunMode.Auto,
            run?.Behavior ?? config.Behavior,
            run?.Strategy ?? config.Strategy,
            run?.Trigger ?? config.Trigger,
            run?.Retention ?? config.Retention,
            run?.ModelContext);
    }
}

public enum CompactionStatus
{
    Started,
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
    string? Reason,
    CompactionRunMode Mode = CompactionRunMode.Auto,
    CompactionBehavior Behavior = CompactionBehavior.Continue) : AgentEvent, IObservabilityEvent;
