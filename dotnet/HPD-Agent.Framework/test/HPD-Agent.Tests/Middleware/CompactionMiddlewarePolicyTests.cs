using System.Collections.Immutable;
using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Middleware.V2;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

public class CompactionMiddlewarePolicyTests
{
    [Fact]
    public async Task BeforeMessageTurnAsync_DisabledMode_DoesNotCompactEvenWhenThresholdIsMet()
    {
        var strategy = new CountingCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig { Mode = CompactionRunMode.Disabled }
            });
        SeedCompactionState(context, messageTurns: 99);
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(0);
        context.ThreadHistory.Should().HaveCount(3);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_ForceMode_CompactsWithoutMeetingThreshold()
    {
        var strategy = new CountingCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig { Mode = CompactionRunMode.Force }
            });
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(1);
        context.ThreadHistory.Should().ContainSingle();
        context.ThreadHistory[0].Text.Should().Be("user-2");
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_PerformedCompaction_EmitsStartedBeforePerformed()
    {
        var coordinator = new EventCoordinator();
        await using var inbox = coordinator.CreateInbox<CompactionEvent>();
        var strategy = new CountingCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig
                {
                    Mode = CompactionRunMode.Force,
                    Behavior = CompactionBehavior.StopAfterCompaction
                }
            },
            coordinator);
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var events = new[]
        {
            await inbox.Reader.ReadAsync(timeout.Token),
            await inbox.Reader.ReadAsync(timeout.Token)
        };
        events.Select(static evt => evt.Status)
            .Should()
            .Equal(CompactionStatus.Started, CompactionStatus.Performed);
        events.Should().OnlyContain(evt =>
            evt.Mode == CompactionRunMode.Force &&
            evt.Behavior == CompactionBehavior.StopAfterCompaction);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_PerformedCompaction_EmitsCheckpointBetweenLifecycleEvents()
    {
        var coordinator = new EventCoordinator();
        var observed = new List<AgentEvent>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = coordinator.SubscribeAny(evt =>
        {
            if (evt is CompactionEvent or ThreadHistoryCompactionCheckpointEvent)
            {
                lock (observed)
                {
                    observed.Add((AgentEvent)evt);
                    if (observed.Count == 3)
                        completed.TrySetResult();
                }
            }

            return ValueTask.CompletedTask;
        });
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig
                {
                    Mode = CompactionRunMode.Force,
                    Behavior = CompactionBehavior.StopAfterCompaction
                }
            },
            coordinator);

        await CreateMiddleware(new CountingCompactionStrategy())
            .BeforeMessageTurnAsync(context, CancellationToken.None);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        observed.Should().SatisfyRespectively(
            first => first.Should().BeOfType<CompactionEvent>()
                .Which.Status.Should().Be(CompactionStatus.Started),
            second => second.Should().BeOfType<ThreadHistoryCompactionCheckpointEvent>(),
            third => third.Should().BeOfType<CompactionEvent>()
                .Which.Status.Should().Be(CompactionStatus.Performed));
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_UnchangedReduction_EmitsSkippedAndPreservesState()
    {
        var coordinator = new EventCoordinator();
        await using var inbox = coordinator.CreateInbox<CompactionEvent>();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig
                {
                    Mode = CompactionRunMode.Force,
                    Behavior = CompactionBehavior.StopAfterCompaction
                }
            },
            coordinator);
        SeedCompactionState(context, messageTurns: 7, inputTokens: 800);
        var middleware = CreateMiddleware(new NoOpCompactionStrategy());

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var started = await inbox.Reader.ReadAsync(timeout.Token);
        var skipped = await inbox.Reader.ReadAsync(timeout.Token);
        started.Status.Should().Be(CompactionStatus.Started);
        skipped.Status.Should().Be(CompactionStatus.Skipped);
        skipped.MessagesRemoved.Should().Be(0);
        skipped.Reason.Should().Be(
            "Nothing to compact because the strategy preserves the 1 most recent user turn.");
        context.ThreadHistory.Should().HaveCount(3);
        context.GetMiddlewareState<CompactionStateData>()!.MessageTurnCount.Should().Be(7);
        context.GetMiddlewareState<CompactionStateData>()!.LastCompaction.Should().BeNull();
        context.State.IsTerminated.Should().BeTrue();
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_ContextWindowTrigger_UsesModelContextWhenTriggerHasNoStaticWindow()
    {
        var strategy = new CountingCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig
                {
                    Trigger = new ContextWindowCompactionTriggerOptions(),
                    ModelContext = new ModelContextWindowOptions
                    {
                        ProviderKey = "openai",
                        ModelId = "small-context",
                        ContextWindow = 1_000
                    }
                }
            });
        SeedCompactionState(context, inputTokens: 800);
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_ContextWindowTriggerWithoutModelContext_Skips()
    {
        var strategy = new CountingCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig
                {
                    Trigger = new ContextWindowCompactionTriggerOptions()
                }
            });
        SeedCompactionState(context, inputTokens: 800);
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(0);
        context.ThreadHistory.Should().HaveCount(3);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_ContextWindowTokenCountTrigger_CompactsWithoutModelContext()
    {
        var strategy = new CountingCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig
                {
                    Trigger = new ContextWindowCompactionTriggerOptions
                    {
                        ThresholdMode = ContextWindowCompactionThresholdMode.TokenCount,
                        TriggerTokenCount = 700
                    }
                }
            });
        SeedCompactionState(context, inputTokens: 800);
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_AutoModeWithValidCachedCompaction_ReusesModelVisibleMessages()
    {
        var coordinator = new EventCoordinator();
        await using var inbox = coordinator.CreateInbox<CompactionEvent>();
        var strategy = new CountingCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig(),
            coordinator,
            [
                Message(ChatRole.User, "old-user", "old-0"),
                Message(ChatRole.Assistant, "old-assistant", "old-1"),
                Message(ChatRole.User, "recent-user", "old-2"),
                Message(ChatRole.User, "new-user", "new-3")
            ]);
        SeedCompactionState(context, snapshot: new CompactionSnapshot
        {
            OriginalMessageIds = ["old-0", "old-1", "old-2"],
            ModelVisibleMessages =
            [
                Message(ChatRole.System, "compacted-boundary", "summary-boundary"),
                Message(ChatRole.Assistant, "summary", "summary-1"),
                Message(ChatRole.User, "recent-user", "old-2")
            ],
            ModelCompactedMessageIds = ["old-0", "old-1"],
            RetainedMessageIds = ["old-2"],
            SummaryContent = "summary",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(0);
        context.ThreadHistory.Select(static message => message.Text)
            .Should()
            .Equal("compacted-boundary", "summary", "recent-user", "new-user");
        context.GetMiddlewareState<CompactionStateData>()!.LastAppliedAt.Should().NotBeNull();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var evt = await inbox.Reader.ReadAsync(timeout.Token);
        evt.Status.Should().Be(CompactionStatus.CacheHit);
        evt.CompactedMessageCount.Should().Be(4);
        evt.MessagesRemoved.Should().Be(2);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_AutoModeWithHighPressureCachedCompaction_ReusesLatestSnapshot()
    {
        var strategy = new CountingCompactionStrategy();
        var bulkyMessages = Enumerable.Range(0, 40)
            .Select(index => Message(
                index % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
                $"old-bulk-{index}-" + new string('x', 4_096),
                $"old-{index}"))
            .ToList();
        var latestSnapshot = Enumerable.Range(1, 10)
            .Select(window => new CompactionSnapshot
            {
                OriginalMessageIds = bulkyMessages.Select(static message => message.MessageId!).ToList(),
                ModelVisibleMessages =
                [
                    Message(ChatRole.Assistant, $"summary-window-{window}", $"summary-{window}"),
                    Message(ChatRole.User, $"retained-window-{window}", $"retained-{window}")
                ],
                ModelCompactedMessageIds = bulkyMessages.Take(39).Select(static message => message.MessageId!).ToList(),
                RetainedMessageIds = [bulkyMessages[^1].MessageId!],
                SummaryContent = $"summary-window-{window}",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-window)
            })
            .Last();
        var context = CreateContext(
            new AgentRunConfig(),
            history: bulkyMessages.Concat([Message(ChatRole.User, "new-after-latest-compaction", "new-after")]).ToList());
        SeedCompactionState(context, snapshot: latestSnapshot);
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(0);
        context.ThreadHistory.Select(static message => message.Text)
            .Should()
            .Equal("summary-window-10", "retained-window-10", "new-after-latest-compaction");
        context.ThreadHistory.Should().NotContain(message => message.Text.Contains("old-bulk-", StringComparison.Ordinal));
        context.ThreadHistory.Sum(static message => message.Text.Length).Should().BeLessThan(200);
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_AutoModeWithAlreadyCompactedPrefix_ReusesCachedModelVisibleMessages()
    {
        var strategy = new CountingCompactionStrategy();
        var cached = new CompactionSnapshot
        {
            OriginalMessageIds = ["old-0", "old-1", "old-2"],
            ModelVisibleMessages =
            [
                Message(ChatRole.Assistant, "summary", "summary-1"),
                Message(ChatRole.User, "retained-user", "old-2")
            ],
            ModelCompactedMessageIds = ["old-0", "old-1"],
            RetainedMessageIds = ["old-2"],
            SummaryContent = "summary",
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        var context = CreateContext(
            new AgentRunConfig(),
            history:
            [
                Message(ChatRole.Assistant, "summary", "summary-1"),
                Message(ChatRole.User, "retained-user", "old-2"),
                Message(ChatRole.User, "first-after-compact", "new-3"),
                Message(ChatRole.Assistant, "first-answer", "new-4"),
                Message(ChatRole.User, "second-after-compact", "new-5")
            ]);
        SeedCompactionState(context, snapshot: cached);
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(0);
        context.ThreadHistory.Select(static message => message.Text)
            .Should()
            .Equal("summary", "retained-user", "first-after-compact", "first-answer", "second-after-compact");
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_ForceMode_StoresModelVisibleMessagesInSnapshot()
    {
        var strategy = new SyntheticSummaryCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig { Mode = CompactionRunMode.Force }
            });
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        var snapshot = context.GetMiddlewareState<CompactionStateData>()!.LastCompaction;
        snapshot.Should().NotBeNull();
        snapshot!.ModelVisibleMessages.Select(static message => message.Text)
            .Should()
            .Equal("cached-summary", "user-2");
    }

    [Fact]
    public async Task BeforeMessageTurnAsync_StopAfterCompaction_TerminatesBeforeModelTurn()
    {
        var strategy = new CountingCompactionStrategy();
        var context = CreateContext(
            new AgentRunConfig
            {
                Compaction = new CompactionRunConfig
                {
                    Mode = CompactionRunMode.Force,
                    Behavior = CompactionBehavior.StopAfterCompaction
                }
            });
        var middleware = CreateMiddleware(strategy);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(1);
        context.State.IsTerminated.Should().BeTrue();
        context.State.TerminationReason.Should().Contain("stopping before model turn");
    }

    [Fact]
    public async Task BeforeIterationAsync_HardRetention_EmitsCheckpointAndCompactsLiveThread()
    {
        var coordinator = new EventCoordinator();
        var observed = new List<AgentEvent>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = coordinator.SubscribeAny(evt =>
        {
            if (evt is CompactionEvent or ThreadHistoryCompactionCheckpointEvent)
            {
                lock (observed)
                {
                    observed.Add((AgentEvent)evt);
                    if (observed.Count == 3)
                        completed.TrySetResult();
                }
            }

            return ValueTask.CompletedTask;
        });

        var messages = new List<ChatMessage>
        {
            Message(ChatRole.User, "old-user", "message-0"),
            Message(ChatRole.Assistant, "old-assistant", "message-1"),
            Message(ChatRole.User, "current-user", "message-2")
        };
        var thread = new HPD.Agent.Thread("session-1", "main");
        thread.Messages.AddRange(messages);
        var context = CreateIterationContext(
            new AgentRunConfig(),
            coordinator,
            messages,
            thread);

        await CreateIterationMiddleware(
                new CountingCompactionStrategy(),
                new CompactThreadHistoryOptions())
            .BeforeIterationAsync(context, CancellationToken.None);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        observed.Should().SatisfyRespectively(
            first => first.Should().BeOfType<CompactionEvent>()
                .Which.Status.Should().Be(CompactionStatus.Started),
            second => second.Should().BeOfType<ThreadHistoryCompactionCheckpointEvent>()
                .Which.Mode.Should().Be(ThreadHistoryCompactionMode.Hard),
            third => third.Should().BeOfType<CompactionEvent>()
                .Which.Status.Should().Be(CompactionStatus.Performed));
        context.Messages.Select(static message => message.Text)
            .Should()
            .Equal("current-user");
        thread.Messages.Select(static message => message.Text)
            .Should()
            .Equal("current-user");
    }

    [Fact]
    public async Task BeforeIterationAsync_SoftRetention_DoesNotEmitThreadCheckpoint()
    {
        var coordinator = new EventCoordinator();
        var observed = new List<AgentEvent>();
        using var subscription = coordinator.SubscribeAny(evt =>
        {
            if (evt is CompactionEvent or ThreadHistoryCompactionCheckpointEvent)
            {
                lock (observed)
                {
                    observed.Add((AgentEvent)evt);
                }
            }

            return ValueTask.CompletedTask;
        });

        var messages = new List<ChatMessage>
        {
            Message(ChatRole.User, "old-user", "message-0"),
            Message(ChatRole.Assistant, "old-assistant", "message-1"),
            Message(ChatRole.User, "current-user", "message-2")
        };
        var thread = new HPD.Agent.Thread("session-1", "main");
        thread.Messages.AddRange(messages);
        var context = CreateIterationContext(
            new AgentRunConfig(),
            coordinator,
            messages,
            thread);

        await CreateIterationMiddleware(
                new CountingCompactionStrategy(),
                new PreserveThreadHistoryOptions())
            .BeforeIterationAsync(context, CancellationToken.None);

        observed.OfType<ThreadHistoryCompactionCheckpointEvent>().Should().BeEmpty();
        observed.OfType<CompactionEvent>().Select(static evt => evt.Status)
            .Should()
            .Equal(CompactionStatus.Started, CompactionStatus.Performed);
        context.Messages.Select(static message => message.Text)
            .Should()
            .Equal("current-user");
        thread.Messages.Select(static message => message.Text)
            .Should()
            .Equal("old-user", "old-assistant", "current-user");
    }

    private static BeforeMessageTurnContext CreateContext(
        AgentRunConfig runConfig,
        EventCoordinator? eventCoordinator = null,
        List<ChatMessage>? history = null)
    {
        history ??=
        [
            Message(ChatRole.User, "user-0", "message-0"),
            Message(ChatRole.Assistant, "assistant-1", "message-1"),
            Message(ChatRole.User, "user-2", "message-2")
        ];

        if (eventCoordinator is null)
        {
            return MiddlewareTestHelpers.CreateBeforeMessageTurnContext(
                conversationHistory: history,
                runConfig: runConfig);
        }

        var context = MiddlewareTestHelpers.CreateAgentContext(eventCoordinator: eventCoordinator);
        return context.AsBeforeMessageTurn(
            new ChatMessage(ChatRole.User, "Test message"),
            history,
            runConfig);
    }

    private static BeforeIterationContext CreateIterationContext(
        AgentRunConfig runConfig,
        EventCoordinator eventCoordinator,
        List<ChatMessage> messages,
        HPD.Agent.Thread thread)
    {
        var context = MiddlewareTestHelpers.CreateAgentContext(
            eventCoordinator: eventCoordinator,
            thread: thread);
        return context.AsBeforeIteration(
            iteration: 1,
            messages,
            new ChatOptions(),
            runConfig);
    }

    private static void SeedCompactionState(
        BeforeMessageTurnContext context,
        int messageTurns = 0,
        long? inputTokens = null,
        CompactionSnapshot? snapshot = null)
    {
        context.UpdateMiddlewareState<CompactionStateData>(_ => new CompactionStateData
        {
            LastCompaction = snapshot,
            MessageTurnCount = messageTurns,
            LastTurnUsage = inputTokens.HasValue
                ? new UsageDetails { InputTokenCount = inputTokens.Value }
                : null,
            LastIterationUsage = ImmutableList<UsageDetails?>.Empty
        });
    }

    private static ChatMessage Message(ChatRole role, string text, string messageId) =>
        new(role, text) { MessageId = messageId };

    private static CompactionMiddleware CreateMiddleware(ICompactionStrategy strategy) =>
        new()
        {
            Strategy = strategy,
            StrategyFactory = (_, _) => strategy,
            Config = new CompactionConfig
            {
                Enabled = true,
                Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 },
                Trigger = new CountCompactionTriggerOptions
                {
                    CountingUnit = HistoryCountingUnit.MessageTurns,
                    TargetCount = 1,
                    Threshold = 0
                }
            }
        };

    private static CompactionMiddleware CreateIterationMiddleware(
        ICompactionStrategy strategy,
        CompactionRetentionOptions retention) =>
        new()
        {
            Strategy = strategy,
            StrategyFactory = (_, _) => strategy,
            Config = new CompactionConfig
            {
                Enabled = true,
                Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 },
                Trigger = new CountCompactionTriggerOptions
                {
                    CountingUnit = HistoryCountingUnit.Messages,
                    TargetCount = 1,
                    Threshold = 0
                },
                Retention = retention
            }
        };

    private sealed class CountingCompactionStrategy : ICompactionStrategy
    {
        public int CallCount { get; private set; }

        public Task<CompactionResult> ReduceAsync(
            IReadOnlyList<ChatMessage> originalMessages,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var retained = originalMessages.TakeLast(1).ToList();
            return Task.FromResult(CompactionResult.FromOriginalAndCompacted(
                originalMessages,
                retained,
                new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 }));
        }
    }

    private sealed class NoOpCompactionStrategy : ICompactionStrategy
    {
        public Task<CompactionResult> ReduceAsync(
            IReadOnlyList<ChatMessage> originalMessages,
            CancellationToken cancellationToken)
            => Task.FromResult(CompactionResult.FromOriginalAndCompacted(
                originalMessages,
                new[]
                {
                    new ChatMessage(ChatRole.System, "Generated compaction context boundary")
                }.Concat(originalMessages).ToList(),
                new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 10 }));
    }

    private sealed class SyntheticSummaryCompactionStrategy : ICompactionStrategy
    {
        public Task<CompactionResult> ReduceAsync(
            IReadOnlyList<ChatMessage> originalMessages,
            CancellationToken cancellationToken)
            => Task.FromResult(CompactionResult.FromOriginalAndCompacted(
                originalMessages,
                [
                    Message(ChatRole.Assistant, "cached-summary", "summary-1"),
                    originalMessages[^1]
                ],
                new SummarizingCompactionOptions { PreserveRecentUserTurnCount = 1 }));
    }
}
