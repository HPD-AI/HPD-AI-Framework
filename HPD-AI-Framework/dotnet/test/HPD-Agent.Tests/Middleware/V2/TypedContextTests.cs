using HPD.Agent;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;
using Xunit;

/// <summary>
/// Tests for typed contexts - compile-time safety, no NULL properties.
/// </summary>
public class TypedContextTests
{
    [Fact]
    public void BeforeIterationContext_MessagesAndOptionsAlwaysAvailable()
    {
        // Arrange
        var context = CreateTestContext();
        var messages = new List<ChatMessage> { new(ChatRole.User, "test") };
        var options = new ChatOptions { Temperature = 0.7f };

        // Act
        var iterContext = context.AsBeforeIteration(0, messages, options, new AgentRunConfig());

        // Assert - no NULL checks needed!
        Assert.NotNull(iterContext.Messages);
        Assert.NotNull(iterContext.Options);
        Assert.Equal(0.7f, iterContext.Options.Temperature);
    }

    [Fact]
    public void BeforeIterationContext_MutableProperties()
    {
        // Arrange
        var context = CreateTestContext();
        var messages = new List<ChatMessage>();
        var options = new ChatOptions();

        // Act
        var iterContext = context.AsBeforeIteration(0, messages, options, new AgentRunConfig());

        // Mutate properties (by design for 90% use case)
        iterContext.Messages.Add(new ChatMessage(ChatRole.System, "Added message"));
        iterContext.Options.Temperature = 0.9f;

        // Assert - mutations visible
        Assert.Single(iterContext.Messages);
        Assert.Equal(0.9f, iterContext.Options.Temperature);
    }

    [Fact]
    public void BeforeIterationContext_ControlSignals()
    {
        // Arrange
        var context = CreateTestContext();
        var iterContext = context.AsBeforeIteration(
            0,
            new List<ChatMessage>(),
            new ChatOptions(),
            new AgentRunConfig());

        // Act
        iterContext.SkipLLMCall = true;
        iterContext.OverrideResponse = new ChatMessage(ChatRole.Assistant, "Cached response");

        // Assert
        Assert.True(iterContext.SkipLLMCall);
        Assert.NotNull(iterContext.OverrideResponse);
        Assert.Equal("Cached response", iterContext.OverrideResponse.Text);
    }

    [Fact]
    public void AfterIterationContext_ToolResultsAlwaysAvailable()
    {
        // Arrange
        var context = CreateTestContext();
        var toolResults = new List<FunctionResultContent>
        {
            new("call1", "success"),
            new("call2", "failure") { Exception = new Exception("Failed") }
        };

        // Act
        var afterContext = context.AsAfterIteration(0, toolResults, new AgentRunConfig());

        // Assert - no NULL checks!
        Assert.Equal(2, afterContext.ToolResults.Count);
        Assert.False(afterContext.AllToolsSucceeded);
        Assert.True(afterContext.AnyToolFailed);
    }

    [Fact]
    public void BeforeFunctionContext_AllPropertiesAvailable()
    {
        // Arrange
        var context = CreateTestContext();
        var function = AIFunctionFactory.Create(() => "test", "TestFunc");
        var args = new Dictionary<string, object?> { ["arg1"] = "value1" };

        // Act
        var funcContext = context.AsBeforeFunction(
            function,
            "call1",
            args,
            new AgentRunConfig(),
            "Toolkit1",
            "Skill1");

        // Assert
        Assert.Equal("TestFunc", funcContext.Function.Name);
        Assert.Equal("call1", funcContext.FunctionCallId);
        Assert.Equal("Toolkit1", funcContext.ToolkitName);
        Assert.Equal("Skill1", funcContext.SkillName);
        Assert.Single(funcContext.Arguments);
    }

    [Fact]
    public void BeforeFunctionContext_BlockExecution()
    {
        // Arrange
        var context = CreateTestContext();
        var function = AIFunctionFactory.Create(() => "test", "TestFunc");
        var funcContext = context.AsBeforeFunction(function, "call1", new Dictionary<string, object?>(), new AgentRunConfig(), null, null);

        // Act
        funcContext.BlockExecution = true;
        funcContext.OverrideResult = "Blocked result";

        // Assert
        Assert.True(funcContext.BlockExecution);
        Assert.Equal("Blocked result", funcContext.OverrideResult);
    }

    [Fact]
    public void AfterFunctionContext_ResultAndExceptionMutable()
    {
        // Arrange
        var context = CreateTestContext();
        var function = AIFunctionFactory.Create(() => "test", "TestFunc");
        var exception = new InvalidOperationException("Test error");

        // Act
        var afterContext = context.AsAfterFunction(function, "call1", "original result", exception, new AgentRunConfig());

        // Middleware can transform result/exception
        afterContext.Result = "transformed result";
        afterContext.Exception = null; // Swallow error

        // Assert
        Assert.Equal("transformed result", afterContext.Result);
        Assert.Null(afterContext.Exception);
        Assert.True(afterContext.IsSuccess);
    }

    [Fact]
    public void ErrorContext_PropertiesAlwaysAvailable()
    {
        // Arrange
        var context = CreateTestContext();
        var exception = new InvalidOperationException("Test");

        // Act
        var errorContext = context.AsError(exception, ErrorSource.ModelCall, 5);

        // Assert
        Assert.Same(exception, errorContext.Error);
        Assert.Equal(ErrorSource.ModelCall, errorContext.Source);
        Assert.Equal(5, errorContext.Iteration);
        Assert.True(errorContext.IsModelError);
        Assert.False(errorContext.IsToolError);
    }

    [Fact]
    public void AfterMessageTurnContext_TurnHistoryMutable()
    {
        // Arrange
        var context = CreateTestContext();
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Response"));
        var turnHistory = new List<ChatMessage>
        {
            new(ChatRole.User, "Question"),
            new(ChatRole.Assistant, "Answer"),
            new(ChatRole.Tool, "Tool result") // Ephemeral
        };

        // Act
        var turnContext = context.AsAfterMessageTurn(response, turnHistory, new AgentRunConfig());

        // Middleware can filter history (e.g., remove ephemeral messages)
        turnContext.TurnHistory.RemoveAll(m => m.Role == ChatRole.Tool);

        // Assert
        Assert.Equal(2, turnContext.TurnHistory.Count);
        Assert.All(turnContext.TurnHistory, m => Assert.NotEqual(ChatRole.Tool, m.Role));
    }

    // Helpers

    private static AgentContext CreateTestContext()
    {
        var state = AgentLoopState.InitialSafe(
            new List<ChatMessage>(),
            "run123",
            "conv123",
            "TestAgent");

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
}
