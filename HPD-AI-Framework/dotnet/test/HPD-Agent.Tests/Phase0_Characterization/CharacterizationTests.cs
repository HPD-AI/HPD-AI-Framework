using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;
using FluentAssertions;
using HPD.Agent;
namespace HPD.Agent.Tests.Phase0_Characterization;

/// <summary>
/// Phase 0: Characterization Tests
///
/// These tests capture the CURRENT behavior of the agent before refactoring.
/// They serve as regression tests to ensure refactoring doesn't break existing functionality.
///
/// Test Coverage:
/// 1. Simple text response (no tools) - See SimpleTextResponseTest.cs
/// 2. Single tool call
/// 3. Multiple parallel tool calls
/// 4. Circuit breaker trigger
/// 5. Max iterations reached
/// 6. Permission denial flow (TODO - requires permission system)
/// 7. Container expansion (TODO - requires container system)
/// </summary>
public class CharacterizationTests : AgentTestBase
{
    /// <summary>
    /// Test 2: Single tool call execution.
    /// Verifies tool call detection, execution, and result handling.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_SingleToolCall_ExecutesAndReturnsResult()
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

        // Create a simple calculator tool
        var calculatorTool = AIFunctionFactory.Create(
            (string expression) => EvaluateExpression(expression),
            name: "Calculator",
            description: "Evaluates mathematical expressions");

        var agent = CreateAgent(
            client: fakeLLM,
            tools: [calculatorTool]);

        var messages = CreateSimpleConversation("What is 2+2?");

        var capturedEvents = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            capturedEvents.Add(evt);
        }

        // Assert - CURRENT behavior
        // Should have 2 iterations (tool call + final response)
        var agentTurnStarts = capturedEvents.OfType<AgentTurnStartedEvent>().ToList();
        agentTurnStarts.Should().HaveCount(2, "there should be 2 iterations: one for tool call, one for final response");

        // Should have tool call events
        capturedEvents.OfType<ToolCallStartEvent>().Should().ContainSingle("there should be exactly one tool call");
        capturedEvents.OfType<ToolCallEndEvent>().Should().ContainSingle("there should be exactly one tool completion");
        capturedEvents.OfType<ToolCallResultEvent>().Should().ContainSingle("there should be exactly one tool result");

        // Should have final text response
        var textDeltas = capturedEvents.OfType<TextDeltaEvent>().ToList();
        var finalText = string.Concat(textDeltas.Select(e => e.Text));
        finalText.Should().Contain("4", "final response should mention the answer");

        // LLM should have been called twice
        fakeLLM.CapturedRequests.Should().HaveCount(2, "LLM called once for initial request, once after tool result");
    }

    /// <summary>
    /// Test 3: Circuit breaker triggers after max consecutive identical calls.
    /// Verifies infinite loop prevention.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_CircuitBreaker_TerminatesOnRepeatedCalls()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // LLM keeps requesting the same failing tool call
        for (int i = 0; i < 6; i++)
        {
            fakeLLM.EnqueueToolCall(
                functionName: "FailingTool",
                callId: $"call_{i}",
                args: new Dictionary<string, object?> { ["input"] = "same_value" });
        }

        // V2: Add final response for after circuit breaker triggers
        fakeLLM.EnqueueTextResponse("Circuit breaker triggered");

        // Create a tool that always fails
        var failingTool = AIFunctionFactory.Create(
            (string input) =>
            {
                throw new Exception("Tool always fails");
            },
            name: "FailingTool",
            description: "A tool that always fails");

        var config = DefaultConfig();
        // Ensure provider is configured
        config.Provider ??= new ProviderConfig();
        config.Provider.ProviderKey = "test";
        config.Provider.ModelName = "test-model";

        var agent = CreateAgent(
            config: config,
            client: fakeLLM,
            circuitBreakerThreshold: 3,
            tools: [failingTool]);

        var messages = CreateSimpleConversation("Use the failing tool");

        var capturedEvents = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            capturedEvents.Add(evt);
        }

        // Assert - CURRENT behavior
        // Should terminate after hitting circuit breaker (3 consecutive identical calls)
        var agentTurnStarts = capturedEvents.OfType<AgentTurnStartedEvent>().ToList();
        agentTurnStarts.Should().HaveCountLessThanOrEqualTo(4, "circuit breaker should stop after max consecutive calls");

        // Should have circuit breaker message or error message
        var textDeltas = capturedEvents.OfType<TextDeltaEvent>().ToList();
        var allText = string.Concat(textDeltas.Select(e => e.Text));
        allText.Should().ContainAny("Circuit breaker", "circuit", "consecutive", "repeated");
    }

    /// <summary>
    /// Test 4: Max iterations limit prevents infinite loops.
    /// Uses MockPermissionHandler to handle ContinuationRequestEvent (auto-denies to terminate at limit).
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_MaxIterations_TerminatesWhenLimitReached()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // Queue more responses than max iterations
        for (int i = 0; i < 10; i++)
        {
            fakeLLM.EnqueueToolCall(
                functionName: "DummyTool",
                callId: $"call_{i}",
                args: new Dictionary<string, object?> { ["index"] = i });
        }

        var dummyTool = AIFunctionFactory.Create(
            (int index) => $"Result {index}",
            name: "DummyTool",
            description: "A dummy tool");

        var config = DefaultConfig();
        config.MaxAgenticIterations = 5; // Set low limit
        // Ensure provider is configured
        config.Provider ??= new ProviderConfig();
        config.Provider.ProviderKey = "test";
        config.Provider.ModelName = "test-model";

        var agent = CreateAgent(
            config: config,
            client: fakeLLM,
            tools: [dummyTool]);

        var messages = CreateSimpleConversation("Use the tool repeatedly");

        // Act - Use MockPermissionHandler to handle continuation requests
        // Configure to deny continuation requests so agent terminates at the limit
        var eventStream = agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken);

        using var permissionHandler = new MockPermissionHandler(agent, eventStream)
            .AutoDenyContinuation(); // Deny continuation to terminate at limit

        await permissionHandler.WaitForCompletionAsync(TimeSpan.FromSeconds(10));

        var capturedEvents = permissionHandler.CapturedEvents;

        // Assert - Behavior after fix
        // Loop condition uses <=, so MaxAgenticIterations=5 means iterations 0-5 (6 total)
        var agentTurnStarts = capturedEvents.OfType<AgentTurnStartedEvent>().ToList();
        agentTurnStarts.Should().HaveCountLessThanOrEqualTo(6, "should respect MaxAgenticIterations limit (loop uses <=)");

        // NOTE: Iteration limits are now enforced by ContinuationPermissionMiddleware
        var agentTurns = agentTurnStarts;
        agentTurns.Max(t => t.Iteration).Should().BeLessThanOrEqualTo(5, "should not exceed configured max iterations");

        // Should have completed successfully (no error)
        capturedEvents.OfType<TextMessageStartEvent>().Should().NotBeEmpty("should have text message start");
        capturedEvents.OfType<TextMessageEndEvent>().Should().NotBeEmpty("should have text message end");
    }

    /// <summary>
    /// Test 5: Multiple parallel tool calls.
    /// Verifies parallel execution and result collection.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_ParallelToolCalls_ExecutesAllTools()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // LLM requests multiple tools in one response (simulating parallel tool calls)
        var args1 = new Dictionary<string, object?> { ["input"] = "A" };
        var args2 = new Dictionary<string, object?> { ["input"] = "B" };
        var args3 = new Dictionary<string, object?> { ["input"] = "C" };

        // Use EnqueueTextWithToolCall for the first, then EnqueueToolCall for others
        fakeLLM.EnqueueTextWithToolCall("Calling tools...", "Tool1", "call_1", args1);
        fakeLLM.EnqueueToolCall("Tool2", "call_2", args2);
        fakeLLM.EnqueueToolCall("Tool3", "call_3", args3);

        // Final response after tool execution
        fakeLLM.EnqueueTextResponse("All tools completed");

        var tool1 = AIFunctionFactory.Create((string input) => $"Tool1: {input}", name: "Tool1");
        var tool2 = AIFunctionFactory.Create((string input) => $"Tool2: {input}", name: "Tool2");
        var tool3 = AIFunctionFactory.Create((string input) => $"Tool3: {input}", name: "Tool3");

        var agent = CreateAgent(
            client: fakeLLM,
            tools: [tool1, tool2, tool3]);

        var messages = CreateSimpleConversation("Use all three tools");

        var capturedEvents = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            capturedEvents.Add(evt);
        }

        // Assert - CURRENT behavior
        // Note: Current implementation may process tool calls sequentially or in parallel
        // We're just documenting what happens, not prescribing behavior
        var toolStartEvents = capturedEvents.OfType<ToolCallStartEvent>().ToList();
        var toolEndEvents = capturedEvents.OfType<ToolCallEndEvent>().ToList();
        var toolResultEvents = capturedEvents.OfType<ToolCallResultEvent>().ToList();

        // All three tools should be executed
        toolStartEvents.Should().HaveCount(3, "all three tools should start execution");
        toolEndEvents.Should().HaveCount(3, "all three tools should complete execution");
        toolResultEvents.Should().HaveCount(3, "all three tools should return results");

        // All tools should have unique call IDs
        var callIds = toolStartEvents.Select(e => e.CallId).ToList();
        callIds.Should().OnlyHaveUniqueItems("each tool call should have a unique ID");
    }

    /// <summary>
    /// Test 6: Consecutive errors trigger termination.
    /// Verifies that MaxConsecutiveErrors limit is enforced.
    /// </summary>
    [Fact]
    public async Task CurrentBehavior_ConsecutiveErrors_TerminatesAfterLimit()
    {
        // Arrange
        var fakeLLM = new FakeChatClient();

        // Queue tool calls that will all fail with errors
        for (int i = 0; i < 6; i++)
        {
            fakeLLM.EnqueueToolCall(
                functionName: "ErrorTool",
                callId: $"call_{i}",
                args: new Dictionary<string, object?> { ["attempt"] = i });
        }

        // V2: Add final response for after error tracking triggers termination
        fakeLLM.EnqueueTextResponse("Maximum consecutive errors exceeded");

        // Create a tool that always throws exceptions
        var errorTool = AIFunctionFactory.Create(
            (int attempt) =>
            {
                throw new InvalidOperationException($"Tool error on attempt {attempt}");
            },
            name: "ErrorTool",
            description: "A tool that always throws errors");

        var config = DefaultConfig();
        config.ErrorHandling ??= new ErrorHandlingConfig();
        config.ErrorHandling.MaxRetries = 3;
        // Ensure provider is configured
        config.Provider ??= new ProviderConfig();
        config.Provider.ProviderKey = "test";
        config.Provider.ModelName = "test-model";

        var agent = CreateAgent(
            config: config,
            client: fakeLLM,
            tools: [errorTool]);

        var messages = CreateSimpleConversation("Use the error tool");

        var capturedEvents = new List<AgentEvent>();

        // Act
        await foreach (var evt in agent.RunAgenticLoopAsync(messages, cancellationToken: TestCancellationToken))
        {
            capturedEvents.Add(evt);
        }

        // Assert - Should terminate after MaxConsecutiveErrors
        var agentTurnStarts = capturedEvents.OfType<AgentTurnStartedEvent>().ToList();
        agentTurnStarts.Should().HaveCountLessThanOrEqualTo(4, "should stop after max consecutive errors (3) + initial turn");

        // Should have error message indicating consecutive errors
        var textDeltas = capturedEvents.OfType<TextDeltaEvent>().ToList();
        var allText = string.Concat(textDeltas.Select(e => e.Text));
        allText.Should().ContainAny("consecutive errors", "Exceeded maximum", "unable to proceed");

        // Should have termination events
        capturedEvents.OfType<TextMessageStartEvent>().Should().NotBeEmpty("should have text message start");
        capturedEvents.OfType<TextMessageEndEvent>().Should().NotBeEmpty("should have text message end");
    }

    // Helper method for calculator
    private static string EvaluateExpression(string expression)
    {
        // Simple evaluation (for testing only)
        if (expression == "2+2")
            return "4";

        return "Unable to evaluate";
    }
}
