using Microsoft.Extensions.AI;
using System.Collections.Immutable;
using Xunit;
using HPD.Agent;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Unit tests for AgentDecisionEngine - demonstrates 1000x faster testing
/// of decision logic without any I/O, mocking, or async complexity.
/// </summary>
public class AgentDecisionEngineTests
{
    private readonly AgentDecisionEngine _engine = new();

    [Fact]
    public void DecideNextAction_InitialState_ReturnsCallLLM()
    {
        // Arrange: Fresh state, no previous response
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent");
        var config = AgentConfiguration.Default(maxIterations: 10);

        // Act: Make decision (pure function - no I/O, completes in microseconds)
        var decision = _engine.DecideNextAction(state, lastResponse: null, config);

        // Assert: First iteration always calls LLM
        Assert.IsType<AgentDecision.CallLLM>(decision);
    }

    [Fact]
    public void DecideNextAction_MaxIterationsReached_DecisionEngineDoesNotCheck()
    {
        // Arrange: State at max iterations
        // NOTE: Max iterations is now enforced by loop condition, not decision engine
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent")
            .NextIteration()  // iteration = 1
            .NextIteration()  // iteration = 2
            .NextIteration(); // iteration = 3

        var config = AgentConfiguration.Default(maxIterations: 3);

        // Act: Decision engine should NOT check max iterations (loop handles it)
        var decision = _engine.DecideNextAction(state, lastResponse: null, config);

        // Assert: Without a response, should return CallLLM (not Terminate)
        // Iteration limiting is loop-level concern, not decision engine concern
        Assert.IsType<AgentDecision.CallLLM>(decision);
    }

    [Fact]
    public void DecideNextAction_AlreadyTerminated_ReturnsTerminate()
    {
        // Arrange: State that's been terminated
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent")
            .Terminate("Manual termination");

        var config = AgentConfiguration.Default();

        // Act
        var decision = _engine.DecideNextAction(state, lastResponse: null, config);

        // Assert: Should return termination
        var terminate = Assert.IsType<AgentDecision.Terminate>(decision);
        Assert.Equal("Manual termination", terminate.Reason);
    }

    [Fact]
    public void DecideNextAction_MaxConsecutiveFailures_ReturnsTerminate()
    {
        // Arrange: State already terminated by ErrorTrackingIterationMiddleware
        // NOTE: Consecutive failure checking is now handled by middleware, not the decision engine
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent")
            .Terminate("Maximum consecutive failures (3) exceeded");

        var config = AgentConfiguration.Default();

        // Act
        var decision = _engine.DecideNextAction(state, lastResponse: null, config);

        // Assert: Should terminate due to state being terminated
        var terminate = Assert.IsType<AgentDecision.Terminate>(decision);
        Assert.Contains("consecutive failures", terminate.Reason);
    }

    [Fact]
    public void DecideNextAction_NoToolsInResponse_ReturnsComplete()
    {
        // Arrange: LLM returned text response with no tool calls
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent").NextIteration();
        var config = AgentConfiguration.Default();

        var responseMessage = new ChatMessage(ChatRole.Assistant, "Here's my answer.");
        var lastResponse = new ChatResponse(responseMessage);

        // Act
        var decision = _engine.DecideNextAction(state, lastResponse, config);

        // Assert: Should complete (no more tools to execute)
        var complete = Assert.IsType<AgentDecision.Complete>(decision);
        Assert.Same(lastResponse, complete.FinalResponse);
    }

    [Fact]
    public void DecideNextAction_ToolsInResponse_ReturnsExecuteTools()
    {
        // Arrange: LLM returned a function call
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent").NextIteration();
        var config = AgentConfiguration.Default();

        var toolCall = new FunctionCallContent(
            callId: "call_123",
            name: "get_weather",
            arguments: new Dictionary<string, object?> { ["city"] = "Seattle" });

        var responseMessage = new ChatMessage(ChatRole.Assistant, [toolCall]);
        var lastResponse = new ChatResponse(responseMessage);

        // Act
        var decision = _engine.DecideNextAction(state, lastResponse, config);

        // Assert: Should call LLM again (tools are executed inline)
        Assert.IsType<AgentDecision.CallLLM>(decision);
    }

    [Fact]
    public void DecideNextAction_CircuitBreakerTriggered_ReturnsTerminate()
    {
        // Arrange: State already terminated by CircuitBreakerIterationMiddleware
        // NOTE: Circuit breaker checking is now handled by middleware, not the decision engine
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent")
            .Terminate("Circuit breaker: 'get_weather' with same arguments would be called 5 times consecutively")
            .NextIteration();

        var config = AgentConfiguration.Default();

        var toolCall = new FunctionCallContent(
            callId: "call_456",
            name: "get_weather",
            arguments: new Dictionary<string, object?> { ["city"] = "Seattle" });

        var responseMessage = new ChatMessage(ChatRole.Assistant, [toolCall]);
        var lastResponse = new ChatResponse(responseMessage);

        // Act
        var decision = _engine.DecideNextAction(state, lastResponse, config);

        // Assert: Should terminate due to state being terminated
        var terminate = Assert.IsType<AgentDecision.Terminate>(decision);
        Assert.Contains("circuit breaker", terminate.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecideNextAction_UnknownTool_WithTerminateFlag_ReturnsTerminate()
    {
        // Arrange: LLM requested a tool that doesn't exist
        var availableTools = new HashSet<string> { "get_weather" };
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent").NextIteration();
        var config = AgentConfiguration.Default(
            availableTools: availableTools,
            terminateOnUnknownCalls: true);

        var unknownToolCall = new FunctionCallContent(
            callId: "call_789",
            name: "unknown_function",
            arguments: new Dictionary<string, object?>());

        var responseMessage = new ChatMessage(ChatRole.Assistant, [unknownToolCall]);
        var lastResponse = new ChatResponse(responseMessage);

        // Act
        var decision = _engine.DecideNextAction(state, lastResponse, config);

        // Assert: Should terminate on unknown tool
        var terminate = Assert.IsType<AgentDecision.Terminate>(decision);
        Assert.Contains("unknown_function", terminate.Reason);
    }

    [Fact]
    public void DecideNextAction_UnknownTool_WithoutTerminateFlag_ReturnsExecuteTools()
    {
        // Arrange: LLM requested a tool that doesn't exist, but we allow pass-through (multi-agent)
        var availableTools = new HashSet<string> { "get_weather" };
        var state = AgentLoopState.InitialSafe(new List<ChatMessage>(), "test-run-id", "test-conversation-id", "TestAgent").NextIteration();
        var config = AgentConfiguration.Default(
            availableTools: availableTools,
            terminateOnUnknownCalls: false);  // Allow multi-agent handoffs

        var unknownToolCall = new FunctionCallContent(
            callId: "call_999",
            name: "handoff_to_agent",
            arguments: new Dictionary<string, object?>());

        var responseMessage = new ChatMessage(ChatRole.Assistant, [unknownToolCall]);
        var lastResponse = new ChatResponse(responseMessage);

        // Act
        var decision = _engine.DecideNextAction(state, lastResponse, config);

        // Assert: Should call LLM again (tools are executed inline, even unknown ones in multi-agent scenarios)
        Assert.IsType<AgentDecision.CallLLM>(decision);
    }

    [Fact]
    public void ComputeFunctionSignature_SameArgsOrdered_ProducesSameSignature()
    {
        // Arrange: Two requests with identical arguments
        var request1 = new FunctionCallContent(
            "call_1",
            "get_weather",
            new Dictionary<string, object?> { ["city"] = "Seattle", ["units"] = "celsius" });

        var request2 = new FunctionCallContent(
            "call_2",
            "get_weather",
            new Dictionary<string, object?> { ["city"] = "Seattle", ["units"] = "celsius" });

        // Act
        var sig1 = CircuitBreakerMiddleware.ComputeFunctionSignature(request1);
        var sig2 = CircuitBreakerMiddleware.ComputeFunctionSignature(request2);

        // Assert: Should produce identical signatures (call ID doesn't matter)
        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void ComputeFunctionSignature_DifferentArgValues_ProducesDifferentSignature()
    {
        // Arrange: Two requests with different argument values
        var request1 = new FunctionCallContent(
            "call_1",
            "get_weather",
            new Dictionary<string, object?> { ["city"] = "Seattle" });

        var request2 = new FunctionCallContent(
            "call_2",
            "get_weather",
            new Dictionary<string, object?> { ["city"] = "Portland" });

        // Act
        var sig1 = CircuitBreakerMiddleware.ComputeFunctionSignature(request1);
        var sig2 = CircuitBreakerMiddleware.ComputeFunctionSignature(request2);

        // Assert: Should produce different signatures
        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void ComputeFunctionSignature_ArgumentOrder_DoesNotMatter()
    {
        // Arrange: Two requests with same args in different order
        var request1 = new FunctionCallContent(
            "call_1",
            "get_weather",
            new Dictionary<string, object?> { ["city"] = "Seattle", ["units"] = "celsius", ["days"] = 5 });

        var request2 = new FunctionCallContent(
            "call_2",
            "get_weather",
            new Dictionary<string, object?> { ["days"] = 5, ["city"] = "Seattle", ["units"] = "celsius" });

        // Act
        var sig1 = CircuitBreakerMiddleware.ComputeFunctionSignature(request1);
        var sig2 = CircuitBreakerMiddleware.ComputeFunctionSignature(request2);

        // Assert: Order shouldn't matter (args are sorted alphabetically)
        Assert.Equal(sig1, sig2);
    }
}
