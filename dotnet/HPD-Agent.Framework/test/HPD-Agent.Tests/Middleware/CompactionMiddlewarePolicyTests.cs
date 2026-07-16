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
    public async Task AutomaticPolicyNull_DisablesAutomaticCompaction()
    {
        var strategy = new CountingStrategy();
        var middleware = CreateMiddleware(strategy, automatic: null);
        var context = CreateContext(new AgentRunConfig());
        SeedUsage(context, messageTurns: 99);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RunPolicyWithNullAutomatic_DisablesConfiguredAutomaticCompaction()
    {
        var strategy = new CountingStrategy();
        var middleware = CreateMiddleware(strategy, AutomaticPolicy());
        var context = CreateContext(new AgentRunConfig
        {
            Compaction = new CompactionRunPolicy { Automatic = null }
        });
        SeedUsage(context, messageTurns: 99);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        strategy.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AutomaticCompaction_HardRetentionMutatesHistoryAfterCheckpointPreparation()
    {
        var strategy = new CountingStrategy();
        var coordinator = new EventCoordinator();
        var thread = new HPD.Agent.Thread { Id = "thread", SessionId = "session" };
        thread.Messages.AddRange(Messages());
        var middleware = CreateMiddleware(
            strategy,
            AutomaticPolicy(),
            new CompactThreadHistoryOptions());
        var context = CreateContext(new AgentRunConfig(), coordinator, thread);
        SeedUsage(context, messageTurns: 99);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        thread.Messages.Select(message => message.MessageId).Should().Equal("m3");
    }

    [Fact]
    public async Task ExplicitCompaction_DoesNotConsultAutomaticTrigger()
    {
        var strategy = new CountingStrategy();
        var middleware = CreateMiddleware(strategy, automatic: null);
        var context = CreateContext(new AgentRunConfig());

        var compacted = await middleware.CompactExplicitAsync(
            context,
            new ThreadCompactionRequest(),
            CancellationToken.None);

        compacted.Should().BeTrue();
        strategy.CallCount.Should().Be(1);
    }

    private static CompactionAutomaticPolicy AutomaticPolicy() => new()
    {
        Trigger = new CountCompactionTriggerOptions
        {
            CountingUnit = HistoryCountingUnit.MessageTurns,
            TargetCount = 1
        }
    };

    private static CompactionMiddleware CreateMiddleware(
        ICompactionStrategy strategy,
        CompactionAutomaticPolicy? automatic,
        CompactionRetentionOptions? retention = null) => new()
    {
        Strategy = strategy,
        StrategyFactory = (_, _) => strategy,
        Config = new CompactionConfig
        {
            Automatic = automatic,
            Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 },
            Retention = retention ?? new PreserveThreadHistoryOptions()
        }
    };

    private static BeforeMessageTurnContext CreateContext(
        AgentRunConfig runConfig,
        EventCoordinator? coordinator = null,
        HPD.Agent.Thread? thread = null)
    {
        thread ??= new HPD.Agent.Thread { Id = "thread", SessionId = "session" };
        if (thread.Messages.Count == 0)
            thread.Messages.AddRange(Messages());
        var baseContext = MiddlewareTestHelpers.CreateAgentContext(
            eventCoordinator: coordinator,
            thread: thread);
        return baseContext.AsBeforeMessageTurn(null, thread.Messages, runConfig);
    }

    private static void SeedUsage(BeforeMessageTurnContext context, int messageTurns) =>
        context.UpdateMiddlewareState<CompactionStateData>(_ => new CompactionStateData
        {
            MessageTurnCount = messageTurns,
            LastIterationUsage = ImmutableList<UsageDetails?>.Empty
        });

    private static List<ChatMessage> Messages() =>
    [
        new(ChatRole.User, "one") { MessageId = "m1" },
        new(ChatRole.Assistant, "two") { MessageId = "m2" },
        new(ChatRole.User, "three") { MessageId = "m3" }
    ];

    private sealed class CountingStrategy : ICompactionStrategy
    {
        public int CallCount { get; private set; }

        public Task<CompactionResult> ReduceAsync(
            IReadOnlyList<ChatMessage> originalMessages,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(CompactionResult.FromOriginalAndCompacted(
                originalMessages,
                originalMessages.TakeLast(1).ToList(),
                new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 }));
        }
    }
}
