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

    private static BeforeMessageTurnContext CreateContext(
        AgentRunConfig runConfig,
        EventCoordinator? eventCoordinator = null)
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "user-0"),
            new(ChatRole.Assistant, "assistant-1"),
            new(ChatRole.User, "user-2")
        };

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

    private static void SeedCompactionState(
        BeforeMessageTurnContext context,
        int messageTurns = 0,
        long? inputTokens = null)
    {
        context.UpdateMiddlewareState<CompactionStateData>(_ => new CompactionStateData
        {
            MessageTurnCount = messageTurns,
            LastTurnUsage = inputTokens.HasValue
                ? new UsageDetails { InputTokenCount = inputTokens.Value }
                : null,
            LastIterationUsage = ImmutableList<UsageDetails?>.Empty
        });
    }

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
}
