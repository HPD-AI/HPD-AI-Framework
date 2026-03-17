using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;

/// <summary>
/// Tests for AgentContext - single unified context with immediate state updates.
/// </summary>
public class AgentContextTests
{
    [Fact]
    public void UpdateState_AppliesImmediately()
    {
        // Arrange
        var initialState = CreateTestState(iteration: 0);
        var context = CreateTestContext(initialState);

        // Act
        context.UpdateState(s => s with { Iteration = 5 });

        // Assert - state updated immediately, no GetPendingState needed!
        Assert.Equal(5, context.Analyze(s => s.Iteration));
    }

    [Fact]
    public void UpdateState_MultipleUpdates_AllApplied()
    {
        // Arrange
        var initialState = CreateTestState(iteration: 0);
        var context = CreateTestContext(initialState);

        // Act
        context.UpdateState(s => s with { Iteration = 1 });
        context.UpdateState(s => s with { Iteration = s.Iteration + 1 });
        context.UpdateState(s => s with { Iteration = s.Iteration + 1 });

        // Assert
        Assert.Equal(3, context.Analyze(s => s.Iteration));
    }

    [Fact]
    public void TypedContext_SharesSameState()
    {
        // Arrange
        var initialState = CreateTestState(iteration: 0);
        var context = CreateTestContext(initialState);

        // Create typed context
        var typedContext = context.AsBeforeIteration(
            0,
            new List<ChatMessage>(),
            new ChatOptions(),
            new AgentRunConfig());

        // Act - update via typed context
        typedContext.UpdateState(s => s with { Iteration = 10 });

        // Assert - base context sees the update
        Assert.Equal(10, context.Analyze(s => s.Iteration));
        Assert.Equal(10, typedContext.State.Iteration);
    }

    [Fact]
    public void AsBeforeIteration_ExposesCorrectProperties()
    {
        // Arrange
        var context = CreateTestContext(CreateTestState(iteration: 5));
        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };
        var options = new ChatOptions { Temperature = 0.7f };

        // Act
        var iterContext = context.AsBeforeIteration(5, messages, options, new AgentRunConfig());

        // Assert
        Assert.Equal(5, iterContext.Iteration);
        Assert.Same(messages, iterContext.Messages);
        Assert.Same(options, iterContext.Options);
        Assert.True(iterContext.IsFirstIteration == false);
    }

    [Fact]
    public void AsAfterIteration_ExposesCorrectProperties()
    {
        // Arrange
        var context = CreateTestContext(CreateTestState(iteration: 1));
        var toolResults = new List<FunctionResultContent>
        {
            new("call1", "result1")
        };

        // Act
        var afterContext = context.AsAfterIteration(1, toolResults, new AgentRunConfig());

        // Assert
        Assert.Single(afterContext.ToolResults);
        Assert.True(afterContext.AllToolsSucceeded);
        Assert.False(afterContext.AnyToolFailed);
    }

    [Fact]
    public void AsBeforeFunction_ExposesCorrectProperties()
    {
        // Arrange
        var context = CreateTestContext(CreateTestState(iteration: 1));
        var function = AIFunctionFactory.Create(() => "test", "TestFunc");
        var args = new Dictionary<string, object?> { ["arg1"] = "value1" };

        // Act
        var funcContext = context.AsBeforeFunction(
            function,
            "call123",
            args,
            new AgentRunConfig(),
            "TestToolkit",
            "TestSkill");

        // Assert
        Assert.Equal("TestFunc", funcContext.Function.Name);
        Assert.Equal("call123", funcContext.FunctionCallId);
        Assert.Equal("TestToolkit", funcContext.ToolkitName);
        Assert.Equal("TestSkill", funcContext.SkillName);
        Assert.Equal("value1", funcContext.Arguments["arg1"]);
    }

    [Fact]
    public void AsError_ExposesCorrectProperties()
    {
        // Arrange
        var context = CreateTestContext(CreateTestState(iteration: 2));
        var exception = new InvalidOperationException("Test error");

        // Act
        var errorContext = context.AsError(exception, ErrorSource.ToolCall, 2);

        // Assert
        Assert.Same(exception, errorContext.Error);
        Assert.Equal(ErrorSource.ToolCall, errorContext.Source);
        Assert.Equal(2, errorContext.Iteration);
        Assert.True(errorContext.IsToolError);
        Assert.False(errorContext.IsModelError);
    }

    // Helpers

    private static AgentContext CreateTestContext(AgentLoopState state)
    {
        var eventCoordinator = new HPD.Events.Core.EventCoordinator();
        return new AgentContext(
            "TestAgent",
            "conv123",
            state,
            eventCoordinator,
            new HPD.Agent.Session("test-session"),
            new HPD.Agent.Session("test-session").CreateBranch(),
            CancellationToken.None);
    }

    private static AgentLoopState CreateTestState(int iteration)
    {
        return AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "run123",
            "conv123",
            "TestAgent") with
        {
            Iteration = iteration
        };
    }
}
