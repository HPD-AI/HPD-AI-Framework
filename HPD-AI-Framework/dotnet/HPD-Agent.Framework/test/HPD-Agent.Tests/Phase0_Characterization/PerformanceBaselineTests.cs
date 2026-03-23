using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;
using FluentAssertions;
using System.Diagnostics;
using HPD.Agent;
namespace HPD.Agent.Tests.Phase0_Characterization;

/// <summary>
/// Phase 0: Performance Baseline Tests
///
/// These tests establish performance benchmarks for the current implementation.
/// They document expected performance before refactoring to detect regressions.
/// </summary>
[Trait("Category", "Performance")]
public class PerformanceBaselineTests : AgentTestBase
{
    /// <summary>
    /// Baseline Test 1: Simple text conversation performance.
    /// Establishes baseline for minimal agent interaction (no tools).
    /// </summary>
    [Fact]
    public async Task Baseline_SimpleTextConversation_CompletesQuickly()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();
        fakeLLM.EnqueueStreamingResponse("Hello", " ", "World", "!");

        var agent = CreateAgent(client: fakeLLM);
        var messages = CreateSimpleConversation("Hello");

        // Act
        var stopwatch = Stopwatch.StartNew();

        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        stopwatch.Stop();

        // Assert - Performance baseline
        // Simple text response should complete quickly (<500ms in most cases)
        // This is a baseline - we're documenting current performance, not enforcing strict limits
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
            "simple text conversation should complete within 1 second (baseline: typically <500ms)");

        // Verify the conversation actually completed
        events.Should().NotBeEmpty("events should have been emitted");
        fakeLLM.CapturedRequests.Should().HaveCount(1, "LLM should have been called once");
    }

    /// <summary>
    /// Baseline Test 2: Single tool call performance.
    /// Establishes baseline for agent + tool execution overhead.
    /// </summary>
    [Fact]
    public async Task Baseline_SingleToolCall_CompletesQuickly()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // LLM requests a tool call
        fakeLLM.EnqueueToolCall(
            functionName: "Calculator",
            callId: "call_1",
            args: new Dictionary<string, object?> { ["expression"] = "2+2" });

        // LLM responds after tool execution
        fakeLLM.EnqueueTextResponse("The answer is 4");

        var calculatorTool = AIFunctionFactory.Create(
            (string expression) => "4",
            name: "Calculator",
            description: "Evaluates mathematical expressions");

        var agent = CreateAgent(client: fakeLLM, tools: [calculatorTool]);
        var messages = CreateSimpleConversation("What is 2+2?");

        // Act
        var stopwatch = Stopwatch.StartNew();

        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        stopwatch.Stop();

        // Assert - Performance baseline
        // Single tool call (2 LLM calls + 1 tool execution) should be fast
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000,
            "single tool call should complete within 2 seconds (baseline: typically <1s)");

        // Verify the tool was actually called
        events.OfType<ToolCallStartEvent>().Should().ContainSingle("one tool should be called");
        fakeLLM.CapturedRequests.Should().HaveCount(2, "LLM should be called twice (initial + after tool)");
    }

    /// <summary>
    /// Baseline Test 3: Multiple iterations performance.
    /// Establishes baseline for agentic loop with multiple tool calls.
    /// </summary>
    [Fact]
    public async Task Baseline_MultipleIterations_CompletesInReasonableTime()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // Queue 3 tool calls (3 iterations)
        for (int i = 0; i < 3; i++)
        {
            fakeLLM.EnqueueToolCall(
                functionName: "DummyTool",
                callId: $"call_{i}",
                args: new Dictionary<string, object?> { ["index"] = i });
        }

        // Final response after all tools
        fakeLLM.EnqueueTextResponse("All tools completed successfully");

        var dummyTool = AIFunctionFactory.Create(
            (int index) => $"Result {index}",
            name: "DummyTool",
            description: "A dummy tool for testing");

        var agent = CreateAgent(client: fakeLLM, tools: [dummyTool]);
        var messages = CreateSimpleConversation("Execute the tool 3 times");

        // Act
        var stopwatch = Stopwatch.StartNew();

        var events = new List<AgentEvent>();
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            events.Add(evt);
        }

        stopwatch.Stop();

        // Assert - Performance baseline
        // 3 iterations (4 LLM calls + 3 tool executions) should complete in reasonable time
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000,
            "multiple iterations should complete within 5 seconds (baseline: typically <3s)");

        // Verify all iterations completed
        events.OfType<ToolCallStartEvent>().Should().HaveCount(3, "three tools should be called");
        events.OfType<AgentTurnStartedEvent>().Should().HaveCount(4, "should have 4 agent turns");
        fakeLLM.CapturedRequests.Should().HaveCount(4, "LLM should be called 4 times");
    }
}
