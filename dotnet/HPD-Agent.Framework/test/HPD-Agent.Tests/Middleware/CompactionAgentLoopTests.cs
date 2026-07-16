using FluentAssertions;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

public sealed class CompactionAgentLoopTests : AgentTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SoftCompaction_ReusesPersistedSnapshotOnFollowingSessionTurn(bool useJsonStore)
    {
        const string sessionId = "compaction-session";
        var tempDirectory = useJsonStore
            ? Path.Combine(Path.GetTempPath(), $"hpd-compaction-{Guid.NewGuid():N}")
            : null;
        ISessionStore store = useJsonStore
            ? new FileSessionStore(tempDirectory!)
            : new InMemorySessionStore();

        try
        {
        var seedClient = new FakeChatClient();
        for (var turn = 0; turn < 6; turn++)
            seedClient.EnqueueTextResponse($"seed-answer-{turn}");

        var seedConfig = DefaultConfig();
        seedConfig.SessionStore = store;
        var seedAgent = CreateAgent(config: seedConfig, client: seedClient);
        await seedAgent.CreateSessionAsync(sessionId, cancellationToken: TestCancellationToken);
        for (var turn = 0; turn < 6; turn++)
        {
            await seedAgent.RunAsync(
                $"seed-history-{turn}",
                sessionId,
                "main",
                cancellationToken: TestCancellationToken);
        }

        var client = new FakeChatClient();
        client.EnqueueTextResponse("first compacted answer");
        client.EnqueueTextResponse("second compacted answer");
        var strategy = new RetainLatestUserTurnStrategy();
        var middleware = CreateCompactionMiddleware(strategy, triggerMessageCount: 10);
        var compactConfig = DefaultConfig();
        compactConfig.SessionStore = store;
        var compactAgent = CreateAgentWithMiddlewares(
            config: compactConfig,
            client: client,
            middlewares: [middleware]);

        await compactAgent.RunAsync(
            "first-after-seed",
            sessionId,
            "main",
            cancellationToken: TestCancellationToken);
        await compactAgent.RunAsync(
            "second-after-seed",
            sessionId,
            "main",
            cancellationToken: TestCancellationToken);

        strategy.CallCount.Should().Be(1, "the second turn should restore and reuse the persisted snapshot");
        client.CapturedRequests.Should().HaveCount(2);
        client.CapturedRequests.Should().OnlyContain(request =>
            request.All(message => !message.Text.Contains("seed-history-", StringComparison.Ordinal)));
        client.CapturedRequests[1].Should().Contain(message => message.Text == "first compacted answer");
        client.CapturedRequests[1].Should().Contain(message => message.Text == "second-after-seed");

        var durableThread = await store.ProjectThreadAsync(sessionId, "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);
        durableThread.Should().NotBeNull();
        durableThread!.Messages.Should().Contain(message =>
            message.Text.Contains("seed-history-", StringComparison.Ordinal),
            "soft compaction must preserve durable history while changing model-visible history");
        }
        finally
        {
            if (tempDirectory is not null && Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SoftCompaction_ReappliesModelVisibleHistoryForEveryToolIteration()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("Echo", "call-1", new Dictionary<string, object?> { ["value"] = "one" });
        client.EnqueueTextResponse("finished");

        var strategy = new RetainLatestUserTurnStrategy();
        var middleware = CreateCompactionMiddleware(strategy, triggerMessageCount: 10);
        var echo = AIFunctionFactory.Create(
            (string value) => value,
            name: "Echo",
            description: "Returns its input.");
        var agent = CreateAgentWithMiddlewares(
            client: client,
            middlewares: [middleware],
            tools: [echo]);
        var messages = CreateBulkyHistory(oldMessageCount: 12);

        await DrainAsync(agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken));

        strategy.CallCount.Should().Be(1, "the cached compaction should be reused after the first reduction");
        client.CapturedRequests.Should().HaveCount(2);
        client.CapturedRequests.Should().OnlyContain(request => RequestExcludesCompactedHistory(request));
        client.CapturedRequests[0].Should().Contain(message => message.MessageId == "current-user");
        client.CapturedRequests[1].SelectMany(message => message.Contents)
            .Should().Contain(content => content is FunctionResultContent);
    }

    [Fact]
    public async Task AutoCompaction_TriggersWhenHistoryCrossesThresholdInsideAgenticTurn()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("Echo", "call-1", new Dictionary<string, object?> { ["value"] = "one" });
        client.EnqueueTextResponse("finished");

        var strategy = new RetainLatestToolGroupStrategy();
        var middleware = CreateCompactionMiddleware(strategy, triggerMessageCount: 3);
        var echo = AIFunctionFactory.Create(
            (string value) => value,
            name: "Echo",
            description: "Returns its input.");
        var agent = CreateAgentWithMiddlewares(
            client: client,
            middlewares: [middleware],
            tools: [echo]);
        var messages = new List<ChatMessage>
        {
            Message(ChatRole.User, "old-user", "old-bulk-0"),
            Message(ChatRole.Assistant, "old-assistant", "old-bulk-1"),
            Message(ChatRole.User, "current-user", "current-user")
        };

        await DrainAsync(agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken));

        client.CapturedRequests.Should().HaveCount(2);
        client.CapturedRequests[0].Should().Contain(message => message.MessageId == "old-bulk-0");
        strategy.CallCount.Should().Be(1, "the tool call grows history beyond the threshold before iteration 1");
        client.CapturedRequests[1].Should().NotContain(message => IsCompactedHistory(message));
        client.CapturedRequests[1].SelectMany(message => message.Contents)
            .Should().Contain(content => content is FunctionResultContent);
    }

    [Fact]
    public async Task SoftCompaction_TenToolIterations_NeverResendsCompactedHistory()
    {
        const int toolIterations = 10;
        var client = new FakeChatClient();
        for (var iteration = 0; iteration < toolIterations; iteration++)
        {
            client.EnqueueToolCall(
                "Echo",
                $"call-{iteration}",
                new Dictionary<string, object?> { ["value"] = iteration.ToString() });
        }
        client.EnqueueTextResponse("finished");

        var strategy = new RetainLatestUserTurnStrategy();
        var middleware = CreateCompactionMiddleware(strategy, triggerMessageCount: 1);
        var echo = AIFunctionFactory.Create(
            (string value) => value,
            name: "Echo",
            description: "Returns its input.");
        var agent = CreateAgentWithMiddlewares(
            client: client,
            middlewares: [middleware],
            circuitBreakerThreshold: null,
            tools: [echo]);
        var messages = CreateBulkyHistory(oldMessageCount: 40);

        await DrainAsync(agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken));

        strategy.CallCount.Should().Be(toolIterations + 1);
        client.CapturedRequests.Should().HaveCount(toolIterations + 1);
        client.CapturedRequests.Should().OnlyContain(request => RequestExcludesCompactedHistory(request));
        client.CapturedRequests[^1].SelectMany(message => message.Contents)
            .OfType<FunctionResultContent>()
            .Should().HaveCount(toolIterations);
    }

    private static CompactionMiddleware CreateCompactionMiddleware(
        ICompactionStrategy strategy,
        int triggerMessageCount) =>
        new()
        {
            Strategy = strategy,
            StrategyFactory = (_, _) => strategy,
            Config = new CompactionConfig
            {
                Strategy = new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 },
                Automatic = new CompactionAutomaticPolicy
                {
                    Trigger = new CountCompactionTriggerOptions
                    {
                        CountingUnit = HistoryCountingUnit.Messages,
                        TargetCount = triggerMessageCount,
                        Threshold = 0
                    }
                },
                Retention = new PreserveThreadHistoryOptions()
            }
        };

    private static List<ChatMessage> CreateBulkyHistory(int oldMessageCount)
    {
        var messages = Enumerable.Range(0, oldMessageCount)
            .Select(index => Message(
                index % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
                $"old-bulk-{index}-" + new string('x', 1_024),
                $"old-bulk-{index}"))
            .ToList();
        messages.Add(Message(ChatRole.User, "current-user", "current-user"));
        return messages;
    }

    private static bool RequestExcludesCompactedHistory(IList<ChatMessage> request) =>
        request.All(message => !IsCompactedHistory(message));

    private static bool IsCompactedHistory(ChatMessage message) =>
        message.Text.Contains("old-bulk-", StringComparison.Ordinal) ||
        (message.MessageId?.StartsWith("old-bulk-", StringComparison.Ordinal) ?? false);

    private static ChatMessage Message(ChatRole role, string text, string messageId) =>
        new(role, text) { MessageId = messageId };

    private static async Task DrainAsync(IAsyncEnumerable<AgentEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class RetainLatestUserTurnStrategy : ICompactionStrategy
    {
        public int CallCount { get; private set; }

        public Task<CompactionResult> ReduceAsync(
            IReadOnlyList<ChatMessage> originalMessages,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var latestUserIndex = originalMessages
                .Select((message, index) => (message, index))
                .Last(pair => pair.message.Role == ChatRole.User)
                .index;
            var retained = originalMessages.Skip(latestUserIndex).ToList();
            return Task.FromResult(CompactionResult.FromOriginalAndCompacted(
                originalMessages,
                retained,
                new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 1 }));
        }
    }

    private sealed class RetainLatestToolGroupStrategy : ICompactionStrategy
    {
        public int CallCount { get; private set; }

        public Task<CompactionResult> ReduceAsync(
            IReadOnlyList<ChatMessage> originalMessages,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var latestToolCallIndex = originalMessages
                .Select((message, index) => (message, index))
                .Where(pair => pair.message.Contents.OfType<FunctionCallContent>().Any())
                .Select(pair => (int?)pair.index)
                .LastOrDefault();
            var retainFrom = latestToolCallIndex ?? originalMessages.Count - 1;
            var retained = originalMessages.Skip(Math.Max(0, retainFrom)).ToList();
            return Task.FromResult(CompactionResult.FromOriginalAndCompacted(
                originalMessages,
                retained,
                new MessageCountingCompactionOptions { PreserveRecentUserTurnCount = 0 }));
        }
    }
}
