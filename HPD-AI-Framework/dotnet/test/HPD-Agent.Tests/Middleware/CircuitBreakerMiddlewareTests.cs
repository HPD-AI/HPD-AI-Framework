using HPD.Agent;
using HPD.Agent.Tests.Middleware.V2;
using static HPD.Agent.Tests.Middleware.V2.MiddlewareTestHelpers;
using HPD.Agent.Middleware;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using System.Collections.Immutable;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Characterization tests for CircuitBreakerMiddleware.
/// These tests document and verify the expected behavior of the circuit breaker.
/// </summary>
public class CircuitBreakerMiddlewareTests
{
    [Fact]
    public async Task BeforeToolExecution_NoToolCalls_DoesNotTrigger()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware
        {
            MaxConsecutiveCalls = 3
        };

        var context = CreateContext(iteration: 0);
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        Assert.False(context.SkipToolExecution);
        Assert.Null(context.OverrideResponse);
        // Properties no longer exists in V2 - state is checked via context.State
    }

    [Fact]
    public async Task BeforeToolExecution_BelowThreshold_DoesNotTrigger()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware
        {
            MaxConsecutiveCalls = 3
        };

        // State shows 1 consecutive call (below threshold of 3)
        var state = CreateStateWithConsecutiveCalls("test_tool", "test_tool({})", 1);
        var context = CreateContext(iteration: 2, state: state);
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        Assert.False(context.SkipToolExecution);
        Assert.Null(context.OverrideResponse);
    }

    [Fact]
    public async Task BeforeToolExecution_AtThreshold_TriggersCircuitBreaker()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware
        {
            MaxConsecutiveCalls = 3
        };

        // State shows 2 consecutive calls, next identical call would be 3rd (at threshold)
        var state = CreateStateWithConsecutiveCalls("test_tool", "test_tool({})", 2);
        var context = CreateContext(iteration: 3, state: state);
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        Assert.True(context.SkipToolExecution);
        Assert.NotNull(context.OverrideResponse);
        Assert.Contains("test_tool", context.OverrideResponse!.Text);
        Assert.Contains("3", context.OverrideResponse!.Text);
        Assert.True(context.Analyze(s => s.IsTerminated));
        Assert.Contains("Circuit breaker", context.Analyze(s => s.TerminationReason) ?? "");
    }

    [Fact]
    public async Task BeforeToolExecution_AboveThreshold_TriggersCircuitBreaker()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware
        {
            MaxConsecutiveCalls = 3
        };

        // State shows 4 consecutive calls (above threshold)
        var state = CreateStateWithConsecutiveCalls("stuck_function", "stuck_function({})", 4);
        var context = CreateContext(iteration: 5, state: state);
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        Assert.True(context.SkipToolExecution);
        Assert.NotNull(context.OverrideResponse);
        Assert.Contains("stuck_function", context.OverrideResponse!.Text);
        // V2: ToolCalls is read-only, middleware sets SkipToolExecution flag instead
    }

    [Fact]
    public async Task BeforeToolExecution_DifferentArguments_DoesNotTrigger()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware
        {
            MaxConsecutiveCalls = 3
        };

        // State shows 2 consecutive calls with signature "test_tool({\"arg\":\"old\"})"
        var state = CreateStateWithConsecutiveCalls("test_tool", "test_tool({\"arg\":\"old\"})", 2);
        var context = CreateContext(iteration: 3, state: state);
        // New call with different arguments
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert - different arguments means count resets, so no trigger
        Assert.False(context.SkipToolExecution);
        Assert.Null(context.OverrideResponse);
    }

    [Fact]
    public async Task BeforeToolExecution_CustomMessageTemplate_UsesTemplate()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware
        {
            MaxConsecutiveCalls = 2,
            TerminationMessageTemplate = "LOOP DETECTED: {toolName} was called {count} times!"
        };

        var state = CreateStateWithConsecutiveCalls("my_function", "my_function({})", 1);
        var context = CreateContext(iteration: 2, state: state);
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        Assert.True(context.SkipToolExecution);
        Assert.NotNull(context.OverrideResponse);
        Assert.Equal("LOOP DETECTED: my_function was called 2 times!", context.OverrideResponse!.Text);
    }

    [Fact]
    public async Task BeforeToolExecution_UpdatesState()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware();
        var context = CreateContext(iteration: 1);
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert - state should be updated (we can't directly verify but no errors thrown)
        Assert.False(context.SkipToolExecution);
    }

    [Fact]
    public async Task BeforeToolExecution_NoToolCalls_DoesNothing()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware();
        var context = CreateContext(iteration: 1);
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert - no changes to context
        Assert.False(context.SkipToolExecution);
        Assert.Null(context.OverrideResponse);
    }

    [Fact]
    public void DefaultConfiguration_UsesReasonableDefaults()
    {
        // Arrange & Act
        var middleware = new CircuitBreakerMiddleware();

        // Assert
        Assert.Equal(3, middleware.MaxConsecutiveCalls);
        Assert.Contains("Circuit breaker triggered", middleware.TerminationMessageTemplate);
        Assert.Contains("{toolName}", middleware.TerminationMessageTemplate);
        Assert.Contains("{count}", middleware.TerminationMessageTemplate);
    }

    [Fact]
    public async Task BeforeToolExecution_EmptyState_DoesNotTrigger()
    {
        // Arrange
        var middleware = new CircuitBreakerMiddleware
        {
            MaxConsecutiveCalls = 2 // Threshold of 2 - first call is count 1
        };

        // Empty state (no tool calls recorded yet)
        var state = CreateEmptyState();
        var context = CreateContext(iteration: 1, state: state);
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert - first call doesn't trigger (predicted count is 1, threshold is 2)
        Assert.False(context.SkipToolExecution);
    }

    //      
    // HELPER METHODS
    //      

    private static AgentLoopState CreateEmptyState()
    {
        return AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");
    }

    private static BeforeToolExecutionContext CreateContext(
        int iteration,
        AgentLoopState? state = null)
    {
        // V2: CircuitBreakerMiddleware uses BeforeToolExecutionAsync
        var actualState = state ?? CreateEmptyState();
        var agentContext = CreateAgentContext(actualState);
        var response = new ChatMessage(ChatRole.Assistant, []);

        // Add a tool call that matches the circuit breaker state (if any)
        var toolCalls = new List<FunctionCallContent>();
        var cbState = actualState.MiddlewareState.CircuitBreaker();
        if (cbState != null && cbState.LastSignaturePerTool.Count > 0)
        {
            // Get the last tool name to create a matching tool call
            var toolName = cbState.LastSignaturePerTool.Keys.First();
            toolCalls.Add(CreateToolCall(toolName));
        }

        return agentContext.AsBeforeToolExecution(response, toolCalls, new AgentRunConfig());
    }

    private static AgentLoopState CreateStateWithConsecutiveCalls(string toolName, string signature, int count)
    {
        var cbState = new CircuitBreakerStateData();
        for (int i = 0; i < count; i++)
        {
            cbState = cbState.RecordToolCall(toolName, signature);
        }

        var state = CreateEmptyState();
        return state with
        {
            MiddlewareState = state.MiddlewareState.WithCircuitBreaker(cbState)
        };
    }

    private static FunctionCallContent CreateToolCall(string name, IDictionary<string, object?>? arguments = null)
    {
        return new FunctionCallContent(
            callId: Guid.NewGuid().ToString(),
            name: name,
            arguments: arguments ?? new Dictionary<string, object?>());
    }

    private static AgentContext CreateAgentContext(AgentLoopState? state = null)
    {
        var agentState = state ?? AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conversation",
            agentName: "TestAgent");

        return new AgentContext(
            "TestAgent",
            "test-conversation",
            agentState,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Branch("test-session"),
            CancellationToken.None);
    }

    private static BeforeToolExecutionContext CreateBeforeToolExecutionContext(
        ChatMessage? response = null,
        List<FunctionCallContent>? toolCalls = null,
        AgentLoopState? state = null)
    {
        var agentContext = CreateAgentContext(state);
        response ??= new ChatMessage(ChatRole.Assistant, []);
        toolCalls ??= new List<FunctionCallContent>();
        return agentContext.AsBeforeToolExecution(response, toolCalls, new AgentRunConfig());
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext(
        AgentLoopState? state = null,
        List<ChatMessage>? turnHistory = null)
    {
        var agentContext = CreateAgentContext(state);
        var finalResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Test response"));
        turnHistory ??= new List<ChatMessage>();
        return agentContext.AsAfterMessageTurn(finalResponse, turnHistory, new AgentRunConfig());
    }

}
