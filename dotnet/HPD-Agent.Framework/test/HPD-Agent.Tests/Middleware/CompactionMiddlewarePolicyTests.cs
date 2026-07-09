using System.Collections.Immutable;
using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Middleware.V2;
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

    private static BeforeMessageTurnContext CreateContext(AgentRunConfig runConfig)
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "user-0"),
            new(ChatRole.Assistant, "assistant-1"),
            new(ChatRole.User, "user-2")
        };

        return MiddlewareTestHelpers.CreateBeforeMessageTurnContext(
            conversationHistory: history,
            runConfig: runConfig);
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
            StrategyFactory = _ => strategy,
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
}
