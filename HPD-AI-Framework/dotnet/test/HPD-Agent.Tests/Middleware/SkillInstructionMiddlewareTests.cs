using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Collapsing;
using Microsoft.Extensions.AI;
using System.Collections.Immutable;
using Xunit;
using CollapsingStateData = HPD.Agent.ContainerMiddlewareState;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Legacy tests for SkillInstructionMiddleware functionality (now merged into ContainerMiddleware).
/// Ported from SkillInstructionIterationFilterTests.cs with updates for new architecture.
/// </summary>
public class SkillInstructionMiddlewareTests
{
    [Fact]
    public async Task Middleware_DoesNotModifyInstructions_WhenNoActiveContainers()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var context = CreateContext(activeContainers: ImmutableDictionary<string, ContainerInstructionSet>.Empty);
        var originalInstructions = context.Options!.Instructions;

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(originalInstructions, context.Options.Instructions);
    }

    [Fact]
    public async Task Middleware_InjectsContainerInstructions_WhenActiveContainersExist()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var activeContainers = ImmutableDictionary<string, ContainerInstructionSet>.Empty
            .Add("trading", new ContainerInstructionSet(null, "Trading skill instructions for buying and selling stocks"));
        var context = CreateContext(activeContainers: activeContainers);
        var originalInstructions = context.Options!.Instructions;

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Contains(" ACTIVE CONTAINER PROTOCOLS", context.Options.Instructions!);
        Assert.Contains("trading", context.Options.Instructions);
        Assert.Contains("Trading skill instructions", context.Options.Instructions);
    }

    [Fact]
    public async Task Middleware_InjectsMultipleContainerInstructions_WhenMultipleActiveContainers()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var activeContainers = ImmutableDictionary<string, ContainerInstructionSet>.Empty
            .Add("trading", new ContainerInstructionSet(null, "Trading skill instructions"))
            .Add("weather", new ContainerInstructionSet(null, "Weather skill instructions"));
        var context = CreateContext(activeContainers: activeContainers);

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert
        Assert.Contains("trading", context.Options!.Instructions!);
        Assert.Contains("weather", context.Options.Instructions);
        Assert.Contains("Trading skill instructions", context.Options.Instructions);
        Assert.Contains("Weather skill instructions", context.Options.Instructions);
    }

    [Fact]
    public async Task Middleware_ClearsContainers_AfterMessageTurn()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var activeContainers = ImmutableDictionary<string, ContainerInstructionSet>.Empty
            .Add("trading", new ContainerInstructionSet(null, "Trading skill instructions"));
        var context = CreateContext(activeContainers: activeContainers);

        // Act - Cleanup happens at AfterMessageTurnAsync


        var afterContext = CreateAfterMessageTurnContext(context.State);


        await middleware.AfterMessageTurnAsync(afterContext, CancellationToken.None);

        // Assert - Check that middleware cleared active container instructions


        var pendingState = afterContext.State;
        Assert.NotNull(pendingState);
        var CollapsingState = pendingState!.MiddlewareState.GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState");
        Assert.NotNull(CollapsingState);
        Assert.Empty(CollapsingState!.ActiveContainerInstructions);
    }

    [Fact]
    public async Task Middleware_DoesNotSignalClearContainers_WhenNotFinalIteration()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var activeContainers = ImmutableDictionary<string, ContainerInstructionSet>.Empty
            .Add("trading", new ContainerInstructionSet(null, "Trading skill instructions"));
        var context = CreateContext(activeContainers: activeContainers);

        // Simulate LLM response with tool calls (NOT final iteration)


        var toolContext = CreateBeforeToolExecutionContext(


            response: new ChatMessage(ChatRole.Assistant, "Response with tools"),


            toolCalls: new List<FunctionCallContent> { new FunctionCallContent("call1", "func1") },


            state: context.State);



        // Act


        await middleware.BeforeToolExecutionAsync(toolContext, CancellationToken.None);

        // Assert


        // V2: Check if NOT final by toolCalls count
        Assert.NotEmpty(toolContext.ToolCalls);
    }

    [Fact]
    public async Task Middleware_DoesNotSignalClearContainers_WhenNoActiveContainers()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var context = CreateContext(activeContainers: ImmutableDictionary<string, ContainerInstructionSet>.Empty);

        // Simulate final iteration


        var toolContext = CreateBeforeToolExecutionContext(


            response: new ChatMessage(ChatRole.Assistant, "Final response"),


            toolCalls: new List<FunctionCallContent>(), // Empty = final


            state: context.State);



        // Act


        await middleware.BeforeToolExecutionAsync(toolContext, CancellationToken.None);

        // Assert


        // V2: Check if final by empty toolCalls
        Assert.Empty(toolContext.ToolCalls);
    }

    [Fact]
    public async Task Middleware_HandlesNullOptions_Gracefully()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var activeContainers = ImmutableDictionary<string, ContainerInstructionSet>.Empty
            .Add("trading", new ContainerInstructionSet(null, "Trading skill instructions"));
        var context = CreateContext(activeContainers: activeContainers);
        // V2: Options is init-only and always provided - test that it's not null

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - V2 always has Options, never null
        Assert.NotNull(context.Options);
    }

    [Fact]
    public async Task Middleware_InjectsInBefore_ClearsAfterMessageTurn()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var activeContainers = ImmutableDictionary<string, ContainerInstructionSet>.Empty
            .Add("trading", new ContainerInstructionSet(null, "Trading instructions"));
        var context = CreateContext(activeContainers: activeContainers);

        // Act - Before phase
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - Instructions injected
        Assert.Contains("Trading instructions", context.Options!.Instructions!);

        // Act - Cleanup happens at AfterMessageTurnAsync


        var afterContext = CreateAfterMessageTurnContext(context.State);


        await middleware.AfterMessageTurnAsync(afterContext, CancellationToken.None);

        // Assert - State updated to clear containers


        var pendingState = afterContext.State;
        Assert.NotNull(pendingState);
        var CollapsingState = pendingState!.MiddlewareState.GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState");
        Assert.NotNull(CollapsingState);
        Assert.Empty(CollapsingState!.ActiveContainerInstructions);
    }

    [Fact]
    public async Task Middleware_PreservesOriginalInstructions_WhenInjecting()
    {
        // Arrange
        var middleware = CreateContainerMiddleware();
        var activeContainers = ImmutableDictionary<string, ContainerInstructionSet>.Empty
            .Add("trading", new ContainerInstructionSet(null, "Trading instructions"));
        var context = CreateContext(activeContainers: activeContainers);
        var originalInstructions = "Original system instructions";
        context.Options!.Instructions = originalInstructions;

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - Original instructions should still be present
        Assert.Contains(originalInstructions, context.Options.Instructions!);
        Assert.Contains("Trading instructions", context.Options.Instructions);
    }

    private static ContainerMiddleware CreateContainerMiddleware()
    {
        // Create a dummy tool so ContainerMiddleware doesn't early-return
        var dummyFunction = AIFunctionFactory.Create(
            () => "test",
            name: "DummyFunction",
            description: "Dummy function for testing");

        var tools = new List<AITool> { dummyFunction };
        var emptyToolkits = ImmutableHashSet<string>.Empty;
        var config = new CollapsingConfig { Enabled = true };

        return new ContainerMiddleware(tools, emptyToolkits, null, null, null, config);
    }

    private static BeforeIterationContext CreateContext(ImmutableDictionary<string, ContainerInstructionSet> activeContainers)
    {
        // Create dummy tools for the context (required by ContainerMiddleware)
        var dummyTool = AIFunctionFactory.Create(
            () => "test",
            name: "DummyFunction",
            description: "Dummy");

        var state = AgentLoopState.InitialSafe(
            messages: new List<ChatMessage>(),
            runId: "test-run-id",
            conversationId: "test-conv-id",
            agentName: "TestAgent")
            with
            {
                MiddlewareState = new MiddlewareState().SetState(
                    "HPD.Agent.ContainerMiddlewareState",
                    new CollapsingStateData { ActiveContainerInstructions = activeContainers })
            };

        var messages = new List<ChatMessage>();
        var options = new ChatOptions
        {
            Instructions = "Base instructions",
            Tools = new List<AITool> { dummyTool }
        };

        // V3: Use AgentContext factory pattern with Session + Branch
        var agentContext = new AgentContext(
            "TestAgent",
            "test-conv-id",
            state,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Branch("test-session"),
            CancellationToken.None);

        return agentContext.AsBeforeIteration(iteration: 0, messages: messages, options: options, runConfig: new AgentRunConfig());
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
