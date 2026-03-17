using Xunit;
using HPD.Agent.Middleware;
using HPD.Agent;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Tests for the Collapsed middleware system using the new ConditionalWeakTable-based architecture.
/// Ported from CollapsedFilterSystemTests.cs with updates for middleware pattern.
/// </summary>
public class CollapsedMiddlewareSystemTests
{
    private class TestMiddleware : IAgentMiddleware
    {
        public string Name { get; }
        public bool WasCalled { get; set; }

        public TestMiddleware(string name) => Name = name;

        public Task BeforeIterationAsync(BeforeIterationContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }

        public Task BeforeFunctionAsync(BeforeFunctionContext context, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }

        public Task AfterIterationAsync(AfterIterationContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeToolExecutionAsync(BeforeToolExecutionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterFunctionAsync(AfterFunctionContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    #region MiddlewareScope Enum Tests

    [Fact]
    public void MiddlewareScope_HasCorrectValues()
    {
        // Ensure Collapse values are in correct order for priority
        Assert.Equal(0, (int)MiddlewareScope.Global);
        Assert.Equal(1, (int)MiddlewareScope.Toolkit);
        Assert.Equal(2, (int)MiddlewareScope.Skill);
        Assert.Equal(3, (int)MiddlewareScope.Function);
    }

    #endregion

    #region Collapsed Middleware ShouldExecute Tests

    [Fact]
    public void GlobalMiddleware_AppliesToAllFunctions()
    {
        // Arrange
        var middleware = new TestMiddleware("global").AsGlobal();

        // Act & Assert - should apply to any context
        Assert.True(middleware.ShouldExecute(CreateContext("AnyFunction")));
        Assert.True(middleware.ShouldExecute(CreateContext("AnyFunction", toolName: "AnyToolkit")));
        Assert.True(middleware.ShouldExecute(CreateContext("AnyFunction", toolName: "AnyToolkit", skillName: "AnySkill")));
        Assert.True(middleware.ShouldExecute(CreateContext("AnyFunction", isSkillContainer: true)));
    }

    [Fact]
    public void ToolkitMiddleware_AppliesToToolkitFunctions()
    {
        // Arrange
        var middleware = new TestMiddleware("Toolkit").ForToolkit("FileSystemTools");

        // Act & Assert
        Assert.True(middleware.ShouldExecute(CreateContext("ReadFile", toolName: "FileSystemTools")));
        Assert.False(middleware.ShouldExecute(CreateContext("ReadFile", toolName: "DatabaseToolkit")));
        Assert.False(middleware.ShouldExecute(CreateContext("ReadFile", toolName: null)));
    }

    [Fact]
    public void SkillMiddleware_AppliesToSkillContainer()
    {
        // Arrange
        var middleware = new TestMiddleware("skill").ForSkill("analyze_codebase");

        // Act - skill container itself
        var context = CreateContext(
            functionName: "analyze_codebase",
            toolName: null,
            skillName: null,
            isSkillContainer: true
        );

        // Assert
        Assert.True(middleware.ShouldExecute(context));
    }

    [Fact]
    public void SkillMiddleware_AppliesToReferencedFunctions()
    {
        // Arrange
        var middleware = new TestMiddleware("skill").ForSkill("analyze_codebase");

        // Act - function called by skill
        var context = CreateContext(
            functionName: "ReadFile",
            toolName: "FileSystemTools",
            skillName: "analyze_codebase",
            isSkillContainer: false
        );

        // Assert
        Assert.True(middleware.ShouldExecute(context));
    }

    [Fact]
    public void SkillMiddleware_DoesNotApplyToOtherSkillContainers()
    {
        // Arrange
        var middleware = new TestMiddleware("skill").ForSkill("analyze_codebase");

        // Act - different skill container
        var context = CreateContext(
            functionName: "refactor_code",
            toolName: null,
            skillName: null,
            isSkillContainer: true
        );

        // Assert
        Assert.False(middleware.ShouldExecute(context));
    }

    [Fact]
    public void SkillMiddleware_DoesNotApplyToFunctionsFromOtherSkills()
    {
        // Arrange
        var middleware = new TestMiddleware("skill").ForSkill("analyze_codebase");

        // Act - function called by different skill
        var context = CreateContext(
            functionName: "WriteFile",
            toolName: "FileSystemTools",
            skillName: "refactor_code",
            isSkillContainer: false
        );

        // Assert
        Assert.False(middleware.ShouldExecute(context));
    }

    [Fact]
    public void FunctionMiddleware_AppliesToSpecificFunction()
    {
        // Arrange
        var middleware = new TestMiddleware("function").ForFunction("ReadFile");

        // Act & Assert
        Assert.True(middleware.ShouldExecute(CreateContext("ReadFile")));
        Assert.False(middleware.ShouldExecute(CreateContext("WriteFile")));
    }

    #endregion

    #region Pipeline Integration Tests

    [Fact]
    public async Task Pipeline_FiltersMiddlewaresByCollapse()
    {
        // Arrange
        var globalMiddleware = new TestMiddleware("Global");
        var ToolkitMiddleware = new TestMiddleware("Toolkit");
        var skillMiddleware = new TestMiddleware("Skill");
        var functionMiddleware = new TestMiddleware("Function");

        globalMiddleware.AsGlobal();
        ToolkitMiddleware.ForToolkit("FileSystemTools");
        skillMiddleware.ForSkill("analyze_codebase");
        functionMiddleware.ForFunction("ReadFile");

        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[]
        {
            globalMiddleware,
            ToolkitMiddleware,
            skillMiddleware,
            functionMiddleware
        });

        var context = CreateContext(
            "ReadFile",
            toolName: "FileSystemTools",
            skillName: "analyze_codebase",
            isSkillContainer: false
        );

        // Act
        await pipeline.ExecuteBeforeFunctionAsync(context, CancellationToken.None);

        // Assert - all 4 middlewares should have executed
        Assert.True(globalMiddleware.WasCalled);
        Assert.True(ToolkitMiddleware.WasCalled);
        Assert.True(skillMiddleware.WasCalled);
        Assert.True(functionMiddleware.WasCalled);
    }

    [Fact]
    public async Task Pipeline_SkipsNonApplicableMiddlewares()
    {
        // Arrange
        var globalMiddleware = new TestMiddleware("Global");
        var ToolkitMiddleware = new TestMiddleware("Toolkit");
        var skillMiddleware = new TestMiddleware("Skill");
        var functionMiddleware = new TestMiddleware("Function");

        globalMiddleware.AsGlobal();
        ToolkitMiddleware.ForToolkit("DatabaseToolkit"); // Wrong Toolkit
        skillMiddleware.ForSkill("refactor_code"); // Wrong skill
        functionMiddleware.ForFunction("WriteFile"); // Wrong function

        var pipeline = new AgentMiddlewarePipeline(new IAgentMiddleware[]
        {
            globalMiddleware,
            ToolkitMiddleware,
            skillMiddleware,
            functionMiddleware
        });

        var context = CreateContext(
            "ReadFile",
            toolName: "FileSystemTools",
            skillName: "analyze_codebase",
            isSkillContainer: false
        );

        // Act
        await pipeline.ExecuteBeforeFunctionAsync(context, CancellationToken.None);

        // Assert - only global middleware should execute
        Assert.True(globalMiddleware.WasCalled);
        Assert.False(ToolkitMiddleware.WasCalled);
        Assert.False(skillMiddleware.WasCalled);
        Assert.False(functionMiddleware.WasCalled);
    }

    #endregion

    #region Skill Container Tests

    [Fact]
    public void SkillMiddleware_AppliesToBothContainerAndReferencedFunctions()
    {
        // Arrange
        var middleware = new TestMiddleware("skill").ForSkill("analyze_codebase");

        // Act & Assert - skill container
        var containerContext = CreateContext(
            "analyze_codebase",
            toolName: null,
            skillName: null,
            isSkillContainer: true
        );
        Assert.True(middleware.ShouldExecute(containerContext));

        // Act & Assert - function called by skill
        var funcContext = CreateContext(
            "ReadFile",
            toolName: "FileSystemTools",
            skillName: "analyze_codebase"
        );
        Assert.True(middleware.ShouldExecute(funcContext));

        // Act & Assert - different skill's function
        var otherContext = CreateContext(
            "WriteFile",
            skillName: "refactor_code"
        );
        Assert.False(middleware.ShouldExecute(otherContext));
    }

    [Fact]
    public void MultipleSkills_CanHaveDifferentMiddlewares()
    {
        // Arrange
        var skill1Middleware = new TestMiddleware("skill1").ForSkill("analyze_codebase");
        var skill2Middleware = new TestMiddleware("skill2").ForSkill("refactor_code");

        // Act - function called from skill1
        var skill1Context = CreateContext(
            "ReadFile",
            skillName: "analyze_codebase"
        );

        // Act - function called from skill2
        var skill2Context = CreateContext(
            "ReadFile",
            skillName: "refactor_code"
        );

        // Assert
        Assert.True(skill1Middleware.ShouldExecute(skill1Context));
        Assert.False(skill1Middleware.ShouldExecute(skill2Context));

        Assert.True(skill2Middleware.ShouldExecute(skill2Context));
        Assert.False(skill2Middleware.ShouldExecute(skill1Context));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Middleware_WithoutCollapse_DefaultsToGlobal()
    {
        // Arrange - middleware without explicit Collapse
        var middleware = new TestMiddleware("default");

        // Act & Assert - should behave as global
        Assert.True(middleware.ShouldExecute(CreateContext("AnyFunction")));
        Assert.True(middleware.ShouldExecute(CreateContext("AnyFunction", toolName: "AnyToolkit")));
    }

    [Fact]
    public void ForToolkit_ThrowsOnNullOrEmpty()
    {
        // Arrange
        var middleware = new TestMiddleware("test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => middleware.ForToolkit(null!));
        Assert.Throws<ArgumentException>(() => middleware.ForToolkit(""));
        Assert.Throws<ArgumentException>(() => middleware.ForToolkit("   "));
    }

    [Fact]
    public void ForSkill_ThrowsOnNullOrEmpty()
    {
        // Arrange
        var middleware = new TestMiddleware("test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => middleware.ForSkill(null!));
        Assert.Throws<ArgumentException>(() => middleware.ForSkill(""));
        Assert.Throws<ArgumentException>(() => middleware.ForSkill("   "));
    }

    [Fact]
    public void ForFunction_ThrowsOnNullOrEmpty()
    {
        // Arrange
        var middleware = new TestMiddleware("test");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => middleware.ForFunction(null!));
        Assert.Throws<ArgumentException>(() => middleware.ForFunction(""));
        Assert.Throws<ArgumentException>(() => middleware.ForFunction("   "));
    }

    #endregion

    //     
    // HELPER METHODS
    //     

    private static BeforeFunctionContext CreateContext(
        string functionName,
        string? toolName = null,
        string? skillName = null,
        bool isSkillContainer = false)
    {
        var additionalProps = new Dictionary<string, object?>();
        if (isSkillContainer)
            additionalProps["IsSkillContainer"] = true;

        var options = new HPDAIFunctionFactoryOptions
        {
            Name = functionName,
            Description = "Test function",
            AdditionalProperties = additionalProps
        };

        var function = HPDAIFunctionFactory.Create(
            (args, ct) => Task.FromResult<object?>("test"),
            options);

        var state = AgentLoopState.InitialSafe(
            messages: Array.Empty<ChatMessage>(),
            runId: "test-run",
            conversationId: "test-conv",
            agentName: "TestAgent");

        var agentContext = new AgentContext(
            "TestAgent",
            "test-conv",
            state,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Branch("test-session"),
            CancellationToken.None);

        return agentContext.AsBeforeFunction(
            function: function,
            callId: "test-call",
            arguments: new Dictionary<string, object?>(),
            runConfig: new AgentRunConfig(),
            toolkitName: toolName,
            skillName: skillName);
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
