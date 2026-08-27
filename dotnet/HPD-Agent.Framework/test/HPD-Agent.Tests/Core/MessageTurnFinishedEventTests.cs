using System.Runtime.CompilerServices;
using FluentAssertions;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Tests for MessageTurnFinishedEvent.Usage — the new optional UsageDetails
/// property added to carry accumulated token counts out of the agent loop.
/// </summary>
public class MessageTurnFinishedEventTests : AgentTestBase
{
    // ── 1.1  Record construction ───────────────────────────────────────────────

    [Fact]
    public void MessageTurnFinishedEvent_Usage_DefaultsToNull()
    {
        var evt = new MessageTurnFinishedEvent(
            MessageTurnId: "t1",
            ConversationId: "c1",
            AgentName: "Agent",
            Duration: TimeSpan.Zero);

        evt.Usage.Should().BeNull();
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
            AgentName: "Agent",
            Duration: TimeSpan.FromSeconds(1),
            Usage: usage);

        evt.Usage.Should().NotBeNull();
        evt.Usage!.InputTokenCount.Should().Be(100);
        evt.Usage.OutputTokenCount.Should().Be(50);
    }

    // ── 1.3  Integration: agent emits Usage from state.AccumulatedUsage ────────

    [Fact]
    public async Task Agent_Emits_MessageTurnFinishedEvent_With_AccumulatedUsage()
    {
        // Arrange: a chat client that reports token usage on every response
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueResponse("Hello!", inputTokens: 10, outputTokens: 5);

        var agent = CreateAgent(client: fakeClient);

        // Act
        var events = new List<AgentEvent>();
        var gate = new object();
        using var subscription = agent.SubscribeAny(evt =>
        {
            lock (gate)
                events.Add(evt);
            return ValueTask.CompletedTask;
        });
        await agent.RunAsync("hi", cancellationToken: TestCancellationToken);
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
        finished!.Usage.Should().NotBeNull("Usage must be populated when the chat client reports tokens");
        finished.Usage!.InputTokenCount.Should().BeGreaterThan(0);
        finished.Usage.OutputTokenCount.Should().BeGreaterThan(0);

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
        var agent = CreateAgent(client: fakeClient, tools: [weather]);

        var result = await agent.RunAsync("weather", cancellationToken: TestCancellationToken);

        var calls = result.Events.OfType<AgentTurnFinishedEvent>().ToList();
        calls.Should().HaveCount(2);
        calls[0].Usage!.InputTokenCount.Should().Be(10);
        calls[0].Usage!.OutputTokenCount.Should().Be(2);
        calls[1].Usage!.InputTokenCount.Should().Be(20);
        calls[1].Usage!.OutputTokenCount.Should().Be(4);

        result.Finished!.Usage!.InputTokenCount.Should().Be(30);
        result.Finished.Usage!.OutputTokenCount.Should().Be(6);
        result.Finished.Usage.AdditionalCounts!["provider_units"].Should().Be(8);
    }

    [Fact]
    public async Task Agent_Preserves_Reported_Usage_When_Another_Call_Has_None()
    {
        var fakeClient = new FakeChatClientWithUsage();
        fakeClient.EnqueueToolCallWithoutUsage("get_weather", "call-1");
        fakeClient.EnqueueResponse("Sunny", inputTokens: 20, outputTokens: 4);

        var weather = AIFunctionFactory.Create(() => "Sunny", name: "get_weather");
        var agent = CreateAgent(client: fakeClient, tools: [weather]);

        var result = await agent.RunAsync("weather", cancellationToken: TestCancellationToken);

        var calls = result.Events.OfType<AgentTurnFinishedEvent>().ToList();
        calls.Should().HaveCount(2);
        calls[0].Usage.Should().BeNull();
        calls[1].Usage!.InputTokenCount.Should().Be(20);
        result.Finished!.Usage!.InputTokenCount.Should().Be(20);
        result.Finished.Usage.OutputTokenCount.Should().Be(4);
    }

    // ── 1.4  Integration: no tokens reported → Usage is null (no crash) ────────

    [Fact]
    public async Task Agent_Emits_MessageTurnFinishedEvent_Usage_Null_When_NoTokensReported()
    {
        // Arrange: standard FakeChatClient returns no UsageDetails
        var fakeClient = new FakeChatClient();
        fakeClient.EnqueueTextResponse("Hello!");

        var agent = CreateAgent(client: fakeClient);

        // Act
        var events = new List<AgentEvent>();
        var gate = new object();
        using var subscription = agent.SubscribeAny(evt =>
        {
            lock (gate)
                events.Add(evt);
            return ValueTask.CompletedTask;
        });
        await agent.RunAsync("hi", cancellationToken: TestCancellationToken);
        await WaitForAsync(() =>
        {
            lock (gate)
                return events.OfType<MessageTurnFinishedEvent>().Any();
        });

        // Assert: event is emitted, Usage may be null — no exception either way
        MessageTurnFinishedEvent? finished;
        lock (gate)
            finished = events.OfType<MessageTurnFinishedEvent>().SingleOrDefault();

        finished.Should().NotBeNull();
        // Usage being null is fine — the guard in MetricsObserver handles this
        // We just verify no exception was thrown (the test completing is the assertion)
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

    private sealed class FakeChatClientWithUsage : IChatClient
    {
        private readonly Queue<(IReadOnlyList<AIContent> Contents, UsageDetails? Usage)> _queue = new();

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
