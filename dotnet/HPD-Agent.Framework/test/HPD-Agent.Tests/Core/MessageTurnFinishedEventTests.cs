using System.Runtime.CompilerServices;
using FluentAssertions;
using HPD.Agent.Providers;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Tests for the immutable per-operation message-turn usage summary.
/// </summary>
public class MessageTurnFinishedEventTests : AgentTestBase
{
    // ── 1.1  Record construction ───────────────────────────────────────────────

    [Fact]
    public void MessageTurnFinishedEvent_Usage_IsRequiredAndCanBeEmpty()
    {
        var evt = new MessageTurnFinishedEvent(
            MessageTurnId: "t1",
            ConversationId: "c1",
            AgentId: "agent-1",
            AgentName: "Agent",
            Duration: TimeSpan.Zero,
            Usage: MessageTurnUsageSummary.Empty);

        evt.Usage.Operations.Should().BeEmpty();
    }

    // ── 1.2  Property round-trip ───────────────────────────────────────────────

    [Fact]
    public void MessageTurnFinishedEvent_Usage_CanBeProvided()
    {
        var usage = new UsageDetails
        {
            InputTokenCount = 100,
            OutputTokenCount = 50
        };

        var evt = new MessageTurnFinishedEvent(
            MessageTurnId: "t1",
            ConversationId: "c1",
            AgentId: "agent-1",
            AgentName: "Agent",
            Duration: TimeSpan.FromSeconds(1),
            Usage: Summary(usage));

        evt.Usage.Operations.Should().ContainSingle();
        evt.Usage.Operations[0].Usage!.InputTokenCount.Should().Be(100);
        evt.Usage.Operations[0].Usage!.OutputTokenCount.Should().Be(50);
    }

    // ── 1.3  Integration: agent emits Usage from state.AccumulatedUsage ────────

    [Fact]
    public async Task Agent_Emits_MessageTurnFinishedEvent_With_AccumulatedUsage()
    {
        // Arrange: a chat client that reports token usage on every response
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("Hello!", inputTokens: 10, outputTokens: 5);

        var agent = CreatePersistentAgent(fakeClient);

        // Act
        var events = new List<AgentEvent>();
        var gate = new object();
        using var subscription = agent.SubscribeAny(evt =>
        {
            lock (gate)
                events.Add(evt);
            return ValueTask.CompletedTask;
        });
        await RunPersistedAsync(agent, "hi");
        await WaitForAsync(() =>
        {
            lock (gate)
                return events.OfType<MessageTurnFinishedEvent>().Any();
        });

        // Assert
        MessageTurnFinishedEvent? finished;
        lock (gate)
            finished = events.OfType<MessageTurnFinishedEvent>().SingleOrDefault();

        finished.Should().NotBeNull("agent must emit exactly one MessageTurnFinishedEvent");
        finished!.Usage.Operations.Should().ContainSingle();
        finished.Usage.Operations[0].Usage!.InputTokenCount.Should().BeGreaterThan(0);
        finished.Usage.Operations[0].Usage!.OutputTokenCount.Should().BeGreaterThan(0);

        AgentTurnFinishedEvent? modelCall;
        lock (gate)
            modelCall = events.OfType<AgentTurnFinishedEvent>().SingleOrDefault();

        modelCall.Should().NotBeNull();
        modelCall!.Usage.Should().NotBeNull();
        modelCall.Usage!.InputTokenCount.Should().Be(10);
        modelCall.Usage.OutputTokenCount.Should().Be(5);
        modelCall.ProviderKey.Should().Be("test");
        modelCall.ModelId.Should().Be("fake-model");
        modelCall.ResponseId.Should().Be("response-1");
    }

    [Fact]
    public async Task AfterMessageTurn_FinalizesBeforeSuccessTerminalAndCompletion()
    {
        var order = new List<string>();
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("done", 3, 2);
        var agent = CreateAgentWithMiddlewares(client: fakeClient, middlewares: [new FinalizationOrderMiddleware(order)]);
        using var subscription = agent.SubscribeAny(evt =>
        {
            if (evt is MessageTurnFinishedEvent) order.Add("finished");
            if (evt is AgentCompletionEvent) order.Add("completion");
            return ValueTask.CompletedTask;
        });

        await agent.RunAsync("hi", cancellationToken: TestCancellationToken);

        Assert.True(order.IndexOf("after") < order.IndexOf("finished"));
        Assert.True(order.IndexOf("finished") < order.IndexOf("completion"));
        Assert.Equal(1, order.Count(item => item == "after"));
    }

    [Fact]
    public async Task AfterMessageTurnFailure_EmitsErrorWithoutSuccessTerminal()
    {
        var events = new List<AgentEvent>();
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("done", 3, 2);
        var agent = CreateAgentWithMiddlewares(client: fakeClient, middlewares: [new FailingFinalizationMiddleware()]);
        using var subscription = agent.SubscribeAny(evt => { events.Add(evt); return ValueTask.CompletedTask; });

        await Assert.ThrowsAnyAsync<Exception>(() => agent.RunAsync("hi", cancellationToken: TestCancellationToken));

        Assert.Contains(events, evt => evt is MessageTurnErrorEvent);
        Assert.DoesNotContain(events, evt => evt is MessageTurnFinishedEvent or AgentCompletionEvent);
    }

    [Fact]
    public async Task FinalizerProviderAttempt_IsIncludedInSuccessfulTerminalUsage()
    {
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("done", 3, 2);
        fakeClient.EnqueueResponse("finalized", 5, 4);
        var agent = CreateAgentWithMiddlewares(client: fakeClient, middlewares: [new ProviderCallingFinalizer(false)]);

        var result = await agent.RunAsync("hi", cancellationToken: TestCancellationToken);

        Assert.Contains(result.Finished!.Usage.Operations, operation => operation.OperationKind == ProviderOperationKind.TextToSpeech);
        Assert.Contains(result.Finished.Usage.Operations, operation => operation.OperationKind == ProviderOperationKind.ChatModelResponse);
    }

    [Fact]
    public async Task FinalizerProviderAttempt_IsIncludedInErrorTerminalUsage()
    {
        var events = new List<AgentEvent>();
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("done", 3, 2);
        fakeClient.EnqueueResponse("finalized", 5, 4);
        var agent = CreateAgentWithMiddlewares(client: fakeClient, middlewares: [new ProviderCallingFinalizer(true)]);
        using var subscription = agent.SubscribeAny(evt => { events.Add(evt); return ValueTask.CompletedTask; });

        await Assert.ThrowsAnyAsync<Exception>(() => agent.RunAsync("hi", cancellationToken: TestCancellationToken));

        var usage = Assert.Single(events.OfType<MessageTurnErrorEvent>()).Usage;
        Assert.Contains(usage.Operations, operation => operation.OperationKind == ProviderOperationKind.TextToSpeech);
        Assert.Contains(usage.Operations, operation => operation.OperationKind == ProviderOperationKind.ChatModelResponse);
    }

    [Fact]
    public async Task FinalizerReplacementAndAssistantAppend_RoundTripThroughThreadJournal()
    {
        var store = new InMemorySessionStore();
        var config = DefaultConfig();
        config.SessionStore = store;
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("secret", 3, 2);
        var agent = CreateAgentWithMiddlewares(config, fakeClient, [new HistoryFinalizerMiddleware()]);
        await agent.CreateSessionAsync("session-finalize", cancellationToken: TestCancellationToken);

        await agent.RunAsync("hi", "session-finalize", "main", cancellationToken: TestCancellationToken);
        var events = await store.CollectThreadEventsAsync("session-finalize", "main", TestCancellationToken);
        var replacement = Assert.Single(events!.OfType<ThreadMessageReplacedEvent>());
        Assert.Contains(events.OfType<TextMessageStartEvent>(), start => start.MessageId == replacement.MessageId);
        var projected = await store.ProjectThreadAsync(
            "session-finalize", "main", ThreadProjectionPurpose.ThreadHistory, TestCancellationToken);

        Assert.Contains(events!, evt => evt is ThreadMessageReplacedEvent);
        Assert.Contains(projected!.Messages, message => message.Text == "redacted");
        Assert.Contains(projected.Messages, message => message.Text == "finalizer note");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalizerCannotRemoveOrReorderCommittedMessages(bool reorder)
    {
        var events = new List<AgentEvent>();
        var config = DefaultConfig();
        config.SessionStore = new InMemorySessionStore();
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("done", 3, 2);
        var agent = CreateAgentWithMiddlewares(config, fakeClient, [new InvalidHistoryFinalizer(reorder)]);
        await agent.CreateSessionAsync("session-invalid-history", cancellationToken: TestCancellationToken);
        using var subscription = agent.SubscribeAny(evt => { events.Add(evt); return ValueTask.CompletedTask; });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => agent.RunAsync("hi", "session-invalid-history", "main", cancellationToken: TestCancellationToken));

        Assert.Contains(events, evt => evt is MessageTurnErrorEvent);
        Assert.DoesNotContain(events, evt => evt is MessageTurnFinishedEvent or AgentCompletionEvent);
    }

    [Fact]
    public async Task ReconciliationFailureSuppressesSuccessTerminal()
    {
        var events = new List<AgentEvent>();
        var store = new InMemorySessionStore();
        var config = DefaultConfig();
        config.SessionStore = store;
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("secret", 3, 2);
        var agent = CreateAgentWithMiddlewares(config, fakeClient, [new DuplicateIdentityFinalizer()]);
        await agent.CreateSessionAsync("session-reconcile-failure", cancellationToken: TestCancellationToken);
        using var subscription = agent.SubscribeAny(evt => { events.Add(evt); return ValueTask.CompletedTask; });

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.RunAsync(
            "hi", "session-reconcile-failure", "main", cancellationToken: TestCancellationToken));

        Assert.Contains(events, evt => evt is MessageTurnErrorEvent);
        Assert.DoesNotContain(events, evt => evt is MessageTurnFinishedEvent or AgentCompletionEvent);
    }

    [Fact]
    public async Task FinalMiddlewareStateIsSynchronizedIntoTerminalEvent()
    {
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("done", 3, 2);
        var agent = CreateAgentWithMiddlewares(client: fakeClient, middlewares: [new FinalStateMiddleware()]);

        var result = await agent.RunAsync("hi", cancellationToken: TestCancellationToken);

        Assert.Equal(42, result.Finished!.Iteration);
    }

    [Fact]
    public async Task Unsessioned_accounted_turn_uses_the_agent_owned_ephemeral_journal()
    {
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("Hello!", inputTokens: 10, outputTokens: 5);
        var agent = CreateAgent(client: fakeClient);
        var result = await agent.RunAsync("hi", cancellationToken: TestCancellationToken);

        var attempt = result.Events.OfType<AgentTurnFinishedEvent>().Should().ContainSingle().Subject;
        attempt.ThreadSequenceNumber.Should().BeGreaterThan(0);
        result.Finished!.Usage.Operations.Should().ContainSingle()
            .Which.ThreadSequenceNumber.Should().Be(attempt.ThreadSequenceNumber);
    }

    [Fact]
    public async Task Agent_Accounts_Every_Model_Call_In_A_Tool_Loop()
    {
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueToolCall("get_weather", "call-1", inputTokens: 10, outputTokens: 2,
            additionalCounts: new Dictionary<string, long> { ["provider_units"] = 3 });
        fakeClient.EnqueueResponse("Sunny", inputTokens: 20, outputTokens: 4,
            additionalCounts: new Dictionary<string, long> { ["provider_units"] = 5 });

        var weather = AIFunctionFactory.Create(
            () => "Sunny",
            name: "get_weather");
        var agent = CreatePersistentAgent(fakeClient, weather);

        var result = await RunPersistedAsync(agent, "weather");

        var calls = result.Events.OfType<AgentTurnFinishedEvent>().ToList();
        calls.Should().HaveCount(2);
        calls[0].Usage!.InputTokenCount.Should().Be(10);
        calls[0].Usage!.OutputTokenCount.Should().Be(2);
        calls[1].Usage!.InputTokenCount.Should().Be(20);
        calls[1].Usage!.OutputTokenCount.Should().Be(4);

        result.Finished!.Usage.Operations.Should().HaveCount(2);
        var aggregate = result.Finished.Usage.AggregateCompatibleUsage(ProviderClientFamily.Chat)!;
        aggregate.InputTokenCount.Should().Be(30);
        aggregate.OutputTokenCount.Should().Be(6);
        aggregate.AdditionalCounts!["provider_units"].Should().Be(8);
    }

    [Fact]
    public async Task Agent_Preserves_Reported_Usage_When_Another_Call_Has_None()
    {
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueToolCallWithoutUsage("get_weather", "call-1");
        fakeClient.EnqueueResponse("Sunny", inputTokens: 20, outputTokens: 4);

        var weather = AIFunctionFactory.Create(() => "Sunny", name: "get_weather");
        var agent = CreatePersistentAgent(fakeClient, weather);

        var result = await RunPersistedAsync(agent, "weather");

        var calls = result.Events.OfType<AgentTurnFinishedEvent>().ToList();
        calls.Should().HaveCount(2);
        calls[0].Usage.Should().BeNull();
        calls[1].Usage!.InputTokenCount.Should().Be(20);
        result.Finished!.Usage.Operations.Should().HaveCount(2);
        result.Finished.Usage.Operations[0].Usage.Should().BeNull();
        result.Finished.Usage.Operations[1].Usage!.InputTokenCount.Should().Be(20);
    }

    // ── 1.4  Integration: no tokens reported → Usage is null (no crash) ────────

    [Fact]
    public async Task Agent_Emits_MessageTurnFinishedEvent_Usage_Null_When_NoTokensReported()
    {
        // Arrange: standard FakeChatClient returns no UsageDetails
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("Hello!");

        var agent = CreatePersistentAgent(fakeClient);

        // Act
        var events = new List<AgentEvent>();
        var gate = new object();
        using var subscription = agent.SubscribeAny(evt =>
        {
            lock (gate)
                events.Add(evt);
            return ValueTask.CompletedTask;
        });
        await RunPersistedAsync(agent, "hi");
        await WaitForAsync(() =>
        {
            lock (gate)
                return events.OfType<MessageTurnFinishedEvent>().Any();
        });

        // Assert: the missing-usage attempt is retained rather than erased.
        MessageTurnFinishedEvent? finished;
        lock (gate)
            finished = events.OfType<MessageTurnFinishedEvent>().SingleOrDefault();

        finished.Should().NotBeNull();
        finished!.Usage.Operations.Should().ContainSingle();
        finished.Usage.Operations[0].Usage.Should().BeNull();

        finished.Should().NotBeNull();
        // Usage being null is fine — the guard in MetricsObserver handles this
        // We just verify no exception was thrown (the test completing is the assertion)
    }

    [Fact]
    public async Task Agent_Emits_Failed_Attempt_And_Error_Summary_When_Dispatched_Model_Fails()
    {
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueFailure(new HttpRequestException("provider failed"));
        var agent = CreatePersistentAgent(fakeClient);
        var events = new List<AgentEvent>();
        using var subscription = agent.SubscribeAny(events.Add);

        Func<Task> action = async () => await RunPersistedAsync(agent, "hi");

        await action.Should().ThrowAsync<Exception>();
        var attempts = events.OfType<AgentTurnFinishedEvent>().ToArray();
        attempts.Should().NotBeEmpty();
        attempts.Should().OnlyContain(attempt => attempt.Outcome == ProviderOperationOutcome.Failed && attempt.Usage == null);
        attempts.Should().OnlyContain(attempt => attempt.ThreadSequenceNumber > 0,
            $"observed sequences were {string.Join(",", attempts.Select(attempt => attempt.ThreadSequenceNumber))}");
        attempts.Select(attempt => attempt.OperationId).Should().OnlyHaveUniqueItems();
        attempts.Select(attempt => attempt.LogicalOperationId).Distinct().Should().ContainSingle();
        var terminal = events.OfType<MessageTurnErrorEvent>().Should().ContainSingle().Subject;
        terminal.Usage.Operations.Should().HaveCount(attempts.Length);
    }

    [Fact]
    public async Task Agent_Emits_Cancelled_Attempt_And_Error_Summary_When_Dispatched_Model_IsCancelled()
    {
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueFailure(new OperationCanceledException("provider cancelled"), repeat: 4);
        var agent = CreatePersistentAgent(fakeClient);
        var events = new List<AgentEvent>();
        using var subscription = agent.SubscribeAny(events.Add);

        Func<Task> action = async () => await RunPersistedAsync(agent, "hi");

        await action.Should().ThrowAsync<Exception>();
        var attempts = events.OfType<AgentTurnFinishedEvent>().ToArray();
        attempts.Should().NotBeEmpty();
        attempts.Should().OnlyContain(attempt => attempt.Outcome == ProviderOperationOutcome.Cancelled);
        var terminal = events.OfType<MessageTurnErrorEvent>().Should().ContainSingle().Subject;
        terminal.Usage.Operations.Select(item => item.OperationId)
            .Should().BeEquivalentTo(attempts.Select(attempt => attempt.OperationId));
    }

    private Agent CreatePersistentAgent(IChatClient client, params AIFunction[] tools)
    {
        var config = DefaultConfig();
        config.SessionStore = new InMemorySessionStore();
        return CreateAgent(config, client, tools: tools);
    }

    private async Task<AgentTurnResult> RunPersistedAsync(Agent agent, string text)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        await agent.CreateSessionAsync(sessionId, cancellationToken: TestCancellationToken);
        return await agent.RunAsync(text, sessionId, "main", cancellationToken: TestCancellationToken);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
                return;

            await Task.Delay(10);
        }

        predicate().Should().BeTrue();
    }

    // ── Helper: minimal chat client that populates UsageDetails ───────────────

    private static MessageTurnUsageSummary Summary(UsageDetails usage) => new(
    [
        new("event-1", "t1", 1, "operation-1", null, 1,
            ProviderOperationKind.ChatModelResponse, ProviderClientFamily.Chat,
            ProviderOperationOutcome.Succeeded, usage, "test", "fake-model", "response-1")
    ]);

    private sealed class FinalizationOrderMiddleware(List<string> order) : IAgentMiddleware
    {
        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
        {
            order.Add("after");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingFinalizationMiddleware : IAgentMiddleware
    {
        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("finalizer failed");
    }

    private sealed class ProviderCallingFinalizer(bool failAfterCall) : IAgentMiddleware
    {
        public async Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
        {
            await ProviderOperationAccounting.ExecuteAsync(
                ProviderOperationKind.TextToSpeech,
                ProviderClientFamily.TextToSpeech,
                "test-tts",
                "voice-1",
                () => Task.FromResult(new UsageDetails { OutputTokenCount = 4 }),
                usage => usage);
            if (failAfterCall) throw new InvalidOperationException("finalizer failed after provider call");
        }
    }

    private sealed class HistoryFinalizerMiddleware : IAgentMiddleware
    {
        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
        {
            var assistant = context.TurnHistory.Last(message => message.Role == ChatRole.Assistant);
            Assert.NotNull(assistant.Contents.OfType<TextContent>().FirstOrDefault());
            assistant.Contents.OfType<TextContent>().First().Text = "redacted";
            context.TurnHistory.Add(new ChatMessage(ChatRole.Assistant, "finalizer note"));
            return Task.CompletedTask;
        }
    }

    private sealed class InvalidHistoryFinalizer(bool reorder) : IAgentMiddleware
    {
        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
        {
            if (reorder)
                context.TurnHistory.Reverse();
            else
                context.TurnHistory.RemoveAt(0);
            return Task.CompletedTask;
        }
    }

    private sealed class DuplicateIdentityFinalizer : IAgentMiddleware
    {
        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
        {
            var existing = context.TurnHistory[0];
            context.TurnHistory.Add(new ChatMessage(existing.Role, existing.Contents)
            {
                MessageId = existing.MessageId
            });
            return Task.CompletedTask;
        }
    }

    private sealed class FinalStateMiddleware : IAgentMiddleware
    {
        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
        {
            context.UpdateState(state => state with { Iteration = 42 });
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChatClientWithUsage : IChatClient
    {
        private readonly Queue<(IReadOnlyList<AIContent> Contents, UsageDetails? Usage)> _queue = new();
        private Exception? _nextException;

        public ChatClientMetadata Metadata => new("FakeChatClientWithUsage", null, "fake-model");

        public void EnqueueResponse(
            string text,
            long inputTokens,
            long outputTokens,
            IReadOnlyDictionary<string, long>? additionalCounts = null)
            => _queue.Enqueue(([new TextContent(text)], CreateUsage(inputTokens, outputTokens, additionalCounts)));

        public void EnqueueToolCall(
            string name,
            string callId,
            long inputTokens,
            long outputTokens,
            IReadOnlyDictionary<string, long>? additionalCounts = null)
            => _queue.Enqueue((
                [new FunctionCallContent(callId, name, new Dictionary<string, object?>())],
                CreateUsage(inputTokens, outputTokens, additionalCounts)));

        public void EnqueueToolCallWithoutUsage(string name, string callId)
            => _queue.Enqueue((
                [new FunctionCallContent(callId, name, new Dictionary<string, object?>())],
                null));

        public void EnqueueFailure(Exception exception, int repeat = 1)
        {
            _nextException = exception;
            for (var index = 0; index < repeat; index++)
                _queue.Enqueue(([], null));
        }

        private static UsageDetails CreateUsage(
            long inputTokens,
            long outputTokens,
            IReadOnlyDictionary<string, long>? additionalCounts)
        {
            AdditionalPropertiesDictionary<long>? counts = null;
            if (additionalCounts is not null)
            {
                counts = new AdditionalPropertiesDictionary<long>();
                foreach (var pair in additionalCounts)
                    counts[pair.Key] = pair.Value;
            }

            return new UsageDetails
            {
                InputTokenCount = inputTokens,
                OutputTokenCount = outputTokens,
                AdditionalCounts = counts
            };
        }

        Task<ChatResponse> IChatClient.GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options,
            CancellationToken cancellationToken)
        {
            if (!_queue.TryDequeue(out var item))
                throw new InvalidOperationException("No responses queued.");

            var message = new ChatMessage(ChatRole.Assistant, string.Empty);
            message.Contents.Clear();
            foreach (var content in item.Contents)
                message.Contents.Add(content);

            var response = new ChatResponse([message])
            {
                ModelId = "fake-model",
                ResponseId = "response-1",
                Usage = item.Usage
            };
            return Task.FromResult(response);
        }

        async IAsyncEnumerable<ChatResponseUpdate> IChatClient.GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!_queue.TryDequeue(out var item))
                throw new InvalidOperationException("No responses queued.");

            if (_nextException is { } exception)
            {
                if (_queue.Count == 0)
                    _nextException = null;
                throw exception;
            }

            await Task.Delay(5, cancellationToken);

            yield return new ChatResponseUpdate
            {
                Contents = [.. item.Contents],
                ModelId = "fake-model",
                ResponseId = "response-1"
            };

            if (item.Usage is not null)
            {
                yield return new ChatResponseUpdate
                {
                    Contents = [new UsageContent(item.Usage)],
                    FinishReason = ChatFinishReason.Stop
                };
            }
            else
            {
                yield return new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop };
            }
        }

#pragma warning disable CA1822, IDE0060
        object? IChatClient.GetService(Type serviceType, object? serviceKey) => null;
#pragma warning restore CA1822, IDE0060
        void IDisposable.Dispose() { }
    }
}
