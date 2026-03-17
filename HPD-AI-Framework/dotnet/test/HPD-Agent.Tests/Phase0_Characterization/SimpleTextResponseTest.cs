using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;
using FluentAssertions;
using HPD.Agent;
namespace HPD.Agent.Tests.Phase0_Characterization;

/// <summary>
/// Phase 0: Characterization Test - Simple Text Response
/// This test captures the CURRENT behavior of the agent before refactoring.
/// </summary>
public class SimpleTextResponseTest : AgentTestBase
{
    /// <summary>
    /// Test 1: Simple text response with no tool calls.
    /// Verifies basic agent-LLM communication and event sequence.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_SimpleTextResponse_EmitsCorrectEventSequence()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("Hello", " ", "World", "!");

        var agent = CreateAgent(client: fakeLLM);
        var messages = CreateSimpleConversation("Hello");

        var capturedEvents = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            capturedEvents.Add(evt);
        }

        // Assert - CURRENT behavior (before refactoring):
        // 1. MessageTurnStarted
        // 2. AgentTurnStarted (iteration 0)
        // 3. TextMessageStart
        // 4. TextDelta (multiple)
        // 5. AgentTurnFinished (comes BEFORE TextMessageEnd in current implementation)
        // 6. TextMessageEnd
        // 7. MessageTurnFinished

        var eventTypes = capturedEvents.Select(e => e.GetType().Name).ToList();

        eventTypes.Should().ContainInOrder(
            nameof(MessageTurnStartedEvent),
            nameof(AgentTurnStartedEvent),
            nameof(TextMessageStartEvent));

        // Should have multiple text deltas
        capturedEvents.OfType<TextDeltaEvent>().Should().HaveCountGreaterThanOrEqualTo(1);

        // Current implementation: TextMessageEnd comes BEFORE AgentTurnFinished
        // This documents the current behavior for comparison after refactoring
        eventTypes.Should().ContainInOrder(
            nameof(TextMessageEndEvent),
            nameof(AgentTurnFinishedEvent),
            nameof(MessageTurnFinishedEvent));

        // Verify FakeLLM was called
        fakeLLM.CapturedRequests.Should().HaveCount(1);
        fakeLLM.CapturedRequests[0].Should().ContainSingle(m => m.Role == ChatRole.User);
    }
}
