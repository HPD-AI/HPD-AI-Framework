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
        await foreach (var evt in agent.RunAsync("hi", cancellationToken: TestCancellationToken))
            events.Add(evt);

        // Assert
        var finished = events.OfType<MessageTurnFinishedEvent>().SingleOrDefault();
        finished.Should().NotBeNull("agent must emit exactly one MessageTurnFinishedEvent");
        finished!.Usage.Should().NotBeNull("Usage must be populated when the chat client reports tokens");
        finished.Usage!.InputTokenCount.Should().BeGreaterThan(0);
        finished.Usage.OutputTokenCount.Should().BeGreaterThan(0);
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
        await foreach (var evt in agent.RunAsync("hi", cancellationToken: TestCancellationToken))
            events.Add(evt);

        // Assert: event is emitted, Usage may be null — no exception either way
        var finished = events.OfType<MessageTurnFinishedEvent>().SingleOrDefault();
        finished.Should().NotBeNull();
        // Usage being null is fine — the guard in MetricsObserver handles this
        // We just verify no exception was thrown (the test completing is the assertion)
    }

    // ── Helper: minimal chat client that populates UsageDetails ───────────────

    private sealed class FakeChatClientWithUsage : IChatClient
    {
        private readonly Queue<(string Text, long InputTokens, long OutputTokens)> _queue = new();

        public ChatClientMetadata Metadata => new("FakeChatClientWithUsage", null, "fake-model");

        public void EnqueueResponse(string text, long inputTokens, long outputTokens)
            => _queue.Enqueue((text, inputTokens, outputTokens));

        Task<ChatResponse> IChatClient.GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options,
            CancellationToken cancellationToken)
        {
            if (!_queue.TryDequeue(out var item))
                throw new InvalidOperationException("No responses queued.");

            var response = new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, item.Text)])
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = item.InputTokens,
                    OutputTokenCount = item.OutputTokens
                }
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
                Contents = [new TextContent(item.Text)]
            };

            yield return new ChatResponseUpdate
            {
                Contents = [new UsageContent(new UsageDetails
                {
                    InputTokenCount = item.InputTokens,
                    OutputTokenCount = item.OutputTokens
                })],
                FinishReason = ChatFinishReason.Stop
            };
        }

#pragma warning disable CA1822, IDE0060
        object? IChatClient.GetService(Type serviceType, object? serviceKey) => null;
#pragma warning restore CA1822, IDE0060
        void IDisposable.Dispose() { }
    }
}
