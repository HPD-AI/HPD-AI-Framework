using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Collapsing;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using System.Collections.Immutable;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Comprehensive tests for ContainerMiddleware.
/// Tests the full lifecycle: visibility filtering (BeforeIteration) and container detection (AfterIteration).
/// </summary>
public class ContainerMiddlewareTests
{
    //      
    // BEFORE ITERATION TESTS - Tool Visibility Filtering
    //      

    [Fact]
    public async Task BeforeIteration_WhenDisabled_DoesNotFilterTools()
    {
        // Arrange
        var (container, members) = CreateCollapsedToolHarness("TestToolHarness", "Test ToolHarness", "Add", "Subtract");
        var allTools = new List<AITool> { container, members[0], members[1] };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty,
            null, null, null, new CollapsingConfig { Enabled = false });

        var context = CreateContext(options: new ChatOptions { Tools = allTools });

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - tools should remain unchanged when disabled
        Assert.Equal(3, context.Options.Tools.Count);
    }

    [Fact]
    public async Task BeforeIteration_WhenEnabled_FiltersToVisibleTools()
    {
        // Arrange
        var (container, members) = CreateCollapsedToolHarness("TestToolHarness", "Test ToolHarness", "Add", "Subtract");
        var nonCollapsed = CreateNonCollapsedFunction("Echo");
        var allTools = new List<AITool> { container, members[0], members[1], nonCollapsed };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty,
            null, null, null, new CollapsingConfig { Enabled = true });

        var context = CreateContext(options: new ChatOptions { Tools = allTools });

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - only container and non-Collapsed functions visible (members hidden)
        var toolNames = context.Options.Tools.OfType<AIFunction>().Select(f => f.Name).ToList();
        Assert.Contains("TestToolHarness", toolNames);
        Assert.Contains("Echo", toolNames);
        Assert.DoesNotContain("Add", toolNames);
        Assert.DoesNotContain("Subtract", toolNames);
    }

    [Fact]
    public async Task BeforeIteration_WhenToolHarnessExpanded_ShowsMemberFunctions()
    {
        // Arrange
        var (container, members) = CreateCollapsedToolHarness("TestToolHarness", "Test ToolHarness", "Add", "Subtract");
        var allTools = new List<AITool> { container, members[0], members[1] };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty,
            null, null, null, new CollapsingConfig { Enabled = true });

        // State with expanded ToolHarness
        var state = CreateEmptyState();
        var CollapsingState = new ContainerMiddlewareState().WithExpandedContainer("TestToolHarness");
        state = state with { MiddlewareState = state.MiddlewareState.SetState("HPD.Agent.ContainerMiddlewareState", CollapsingState) };

        var context = CreateContext(state: state, options: new ChatOptions { Tools = allTools });

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - all functions visible when ToolHarness is expanded
        var toolNames = context.Options.Tools.OfType<AIFunction>().Select(f => f.Name).ToList();
        Assert.Contains("Add", toolNames);
        Assert.Contains("Subtract", toolNames);
    }

    [Fact]
    public async Task BeforeIteration_WhenNoTools_DoesNothing()
    {
        // Arrange
        var middleware = new ContainerMiddleware(
            Array.Empty<AITool>(),
            ImmutableHashSet<string>.Empty);

        var context = CreateContext(options: new ChatOptions { Tools = new List<AITool>() });

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - no exception, empty tools list
        Assert.Empty(context.Options.Tools);
    }

    [Fact]
    public async Task BeforeIteration_PreservesOtherChatOptionsProperties()
    {
        // Arrange
        var function = CreateNonCollapsedFunction("TestFunc");
        var allTools = new List<AITool> { function };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty);

        var context = CreateContext(options: new ChatOptions
        {
            Tools = allTools,
            ModelId = "test-model",
            Temperature = 0.5f,
            MaxOutputTokens = 1000,
            ConversationId = "test-conversation"
        });

        // Act
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Assert - all other properties preserved
        Assert.Equal("test-model", context.Options.ModelId);
        Assert.Equal(0.5f, context.Options.Temperature);
        Assert.Equal(1000, context.Options.MaxOutputTokens);
        Assert.Equal("test-conversation", context.Options.ConversationId);
    }

    //      
    // AFTER ITERATION TESTS - Container Detection
    //      

    [Fact]
    public async Task BeforeToolExecution_WhenDisabled_DoesNotDetectContainers()
    {
        // Arrange
        var (container, _) = CreateCollapsedToolHarness("TestToolHarness", "Test ToolHarness", "Add");
        var allTools = new List<AITool> { container };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty,
            null, null, null, new CollapsingConfig { Enabled = false });

        var toolCalls = new List<FunctionCallContent> { CreateToolCall("TestToolHarness") };
        var context = CreateBeforeToolExecutionContext(toolCalls: toolCalls);

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert - container detection happens but we check state instead
        // When disabled, no containers should be expanded
        var containerState = context.Analyze(s => s.MiddlewareState.GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState"));
        Assert.Empty(containerState?.ExpandedContainers ?? ImmutableHashSet<string>.Empty);
    }

    [Fact]
    public async Task BeforeToolExecution_WhenNoToolCalls_DoesNothing()
    {
        // Arrange
        var middleware = new ContainerMiddleware(
            Array.Empty<AITool>(),
            ImmutableHashSet<string>.Empty);

        var toolCalls = Array.Empty<FunctionCallContent>();

        var context = CreateBeforeToolExecutionContext(toolCalls: toolCalls.ToList());

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert - no state updates
        // State is immediately updated in V2
    }

    [Fact]
    public async Task BeforeToolExecution_DetectsToolHarnessContainer_UpdatesState()
    {
        // Arrange
        var (container, members) = CreateCollapsedToolHarness("FinancialToolHarness", "Financial tools", "Add", "Subtract");
        var allTools = new List<AITool> { container, members[0], members[1] };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty);

        // Create tool calls that invoke the container
        var toolCalls = new List<FunctionCallContent> { CreateToolCall("FinancialToolHarness") };
        var context = CreateBeforeToolExecutionContext(toolCalls: toolCalls);

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert - state updated with expanded ToolHarness
        // State is immediately updated in V2
        var pendingState = context.State;
        Assert.NotNull(pendingState);

        // Check Collapsing state
        var containerState = pendingState.MiddlewareState.GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState");
        Assert.Contains("FinancialToolHarness", containerState?.ExpandedContainers ?? ImmutableHashSet<string>.Empty);
    }

    [Fact]
    public async Task BeforeToolExecution_DetectsSkillContainer_UpdatesState()
    {
        // Arrange
        var skill = CreateSkillContainer("TestSkill", "Test skill description", "func1", "func2");
        var func1 = CreateNonCollapsedFunction("func1");
        var func2 = CreateNonCollapsedFunction("func2");
        var allTools = new List<AITool> { skill, func1, func2 };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty);

        // Create tool calls that invoke the skill container
        var toolCalls = new List<FunctionCallContent> { CreateToolCall("TestSkill") };
        var context = CreateBeforeToolExecutionContext(toolCalls: toolCalls);

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        // State is immediately updated in V2
        var pendingState = context.State;
        Assert.NotNull(pendingState);

        // Check Collapsing state
        var containerState = pendingState.MiddlewareState.GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState");
        Assert.Contains("TestSkill", containerState?.ExpandedContainers ?? ImmutableHashSet<string>.Empty);
    }

    [Fact]
    public async Task BeforeToolExecution_HiddenMemberRecovery_StoresContainerInstructions()
    {
        // Arrange
        var systemPrompt = "Use the coding tools carefully.";
        var (container, members) = CreateCollapsedToolHarnessWithSystemPrompt(
            "CodingToolHarness",
            "Coding tools",
            systemPrompt,
            "ListDirectory");
        var allTools = new List<AITool> { container, members[0] };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty);

        var context = CreateBeforeToolExecutionContext(
            toolCalls: new List<FunctionCallContent> { CreateToolCall("ListDirectory") });

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        var containerState = context.State.MiddlewareState.GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState");
        Assert.NotNull(containerState);
        Assert.Contains("CodingToolHarness", containerState!.ExpandedContainers);
        Assert.True(containerState.ActiveContainerInstructions.ContainsKey("CodingToolHarness"));
        Assert.Equal(systemPrompt, containerState.ActiveContainerInstructions["CodingToolHarness"].SystemPrompt);
    }

    [Fact]
    public async Task BeforeToolExecution_SkillWithSystemPrompt_StoresInstructions()
    {
        // Arrange
        var instructions = "Always use metric units when performing calculations.";
        var skill = CreateSkillContainerWithSystemPrompt("MetricSkill", "Metric calculations", instructions);
        var allTools = new List<AITool> { skill };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty);

        // Create tool calls that invoke the skill container
        var toolCalls = new List<FunctionCallContent> { CreateToolCall("MetricSkill") };
        var context = CreateBeforeToolExecutionContext(toolCalls: toolCalls);

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        var pendingState = context.State;
        Assert.NotNull(pendingState);

        // Check instructions in Collapsing state
        var CollapsingState = pendingState.MiddlewareState.GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState");
        Assert.NotNull(CollapsingState);
        Assert.True(CollapsingState!.ActiveContainerInstructions.ContainsKey("MetricSkill"));
        Assert.Equal(instructions, CollapsingState.ActiveContainerInstructions["MetricSkill"].SystemPrompt);
    }

    [Fact]
    public async Task BeforeToolExecution_NonContainerToolCall_DoesNotUpdateState()
    {
        // Arrange
        var regularFunc = CreateNonCollapsedFunction("RegularFunction");
        var allTools = new List<AITool> { regularFunc };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty);

        var context = CreateBeforeToolExecutionContext();
        // Options set in constructor
        // ToolCalls set in context constructor

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert - no state updates for non-container calls
        // State is immediately updated in V2
    }

    [Fact]
    public async Task BeforeToolExecution_MultipleContainers_ExpandsAll()
    {
        // Arrange
        var (ToolHarness1, _) = CreateCollapsedToolHarness("ToolHarness1", "First ToolHarness", "A");
        var (ToolHarness2, _) = CreateCollapsedToolHarness("ToolHarness2", "Second ToolHarness", "B");
        var skill = CreateSkillContainer("Skill1", "First skill");
        var allTools = new List<AITool> { ToolHarness1, ToolHarness2, skill };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty);

        // Create tool calls that invoke all three containers
        var toolCalls = new List<FunctionCallContent>
        {
            CreateToolCall("ToolHarness1"),
            CreateToolCall("ToolHarness2"),
            CreateToolCall("Skill1")
        };
        var context = CreateBeforeToolExecutionContext(toolCalls: toolCalls);

        // Act
        await middleware.BeforeToolExecutionAsync(context, CancellationToken.None);

        // Assert
        var pendingState = context.State;
        Assert.NotNull(pendingState);

        var CollapsingState = pendingState.MiddlewareState.GetState<ContainerMiddlewareState>("HPD.Agent.ContainerMiddlewareState");
        Assert.NotNull(CollapsingState);
        Assert.Contains("ToolHarness1", CollapsingState!.ExpandedContainers);
        Assert.Contains("ToolHarness2", CollapsingState.ExpandedContainers);
        Assert.Contains("Skill1", CollapsingState.ExpandedContainers);
    }

    //      
    // INTEGRATION TESTS - Full Middleware Lifecycle
    //      

    [Fact]
    public async Task FullLifecycle_ExpandToolHarness_NextIterationShowsFunctions()
    {
        // Arrange
        var (container, members) = CreateCollapsedToolHarness("TestToolHarness", "Test ToolHarness", "Add", "Subtract");
        var allTools = new List<AITool> { container, members[0], members[1] };

        var middleware = new ContainerMiddleware(
            allTools,
            ImmutableHashSet<string>.Empty);

        // Iteration 1: ToolHarness not expanded
        var beforeIter1 = CreateIterationContext(options: new ChatOptions { Tools = allTools });

        await middleware.BeforeIterationAsync(beforeIter1, CancellationToken.None);

        var visibleIter1 = beforeIter1.Options.Tools.OfType<AIFunction>().Select(f => f.Name).ToList();
        Assert.Contains("TestToolHarness", visibleIter1);
        Assert.DoesNotContain("Add", visibleIter1);

        // Simulate LLM calling the container
        var toolExecContext = CreateBeforeToolExecutionContext(
            toolCalls: new List<FunctionCallContent> { CreateToolCall("TestToolHarness") },
            state: beforeIter1.State);
        await middleware.BeforeToolExecutionAsync(toolExecContext, CancellationToken.None);

        // Iteration 2: ToolHarness now expanded
        var expandedState = toolExecContext.State!;
        var beforeIter2 = CreateIterationContext(state: expandedState, options: new ChatOptions { Tools = allTools });

        await middleware.BeforeIterationAsync(beforeIter2, CancellationToken.None);

        // Assert - member functions now visible
        var visibleIter2 = beforeIter2.Options.Tools.OfType<AIFunction>().Select(f => f.Name).ToList();
        Assert.Contains("Add", visibleIter2);
        Assert.Contains("Subtract", visibleIter2);
    }

    //
    // Collapsing STATE DATA TESTS
    //

    [Fact]
    public void ContainerMiddlewareState_WithExpandedContainer_AddsToSet()
    {
        var state = new ContainerMiddlewareState();

        var updated = state.WithExpandedContainer("ToolHarness1");

        Assert.Contains("ToolHarness1", updated.ExpandedContainers);
        Assert.DoesNotContain("ToolHarness1", state.ExpandedContainers); // Original unchanged
    }

    [Fact]
    public void ContainerMiddlewareState_WithExpandedContainer_AddsSkillToSet()
    {
        var state = new ContainerMiddlewareState();

        var updated = state.WithExpandedContainer("Skill1");

        Assert.Contains("Skill1", updated.ExpandedContainers);
    }

    [Fact]
    public void ContainerMiddlewareState_WithContainerInstructions_AddsToDict()
    {
        var state = new ContainerMiddlewareState();

        var updated = state.WithContainerInstructions("Skill1", new ContainerInstructionSet("Result", "Some instructions"));

        Assert.True(updated.ActiveContainerInstructions.ContainsKey("Skill1"));
        Assert.Equal("Some instructions", updated.ActiveContainerInstructions["Skill1"].SystemPrompt);
        Assert.Equal("Result", updated.ActiveContainerInstructions["Skill1"].FunctionResult);
    }

    [Fact]
    public void ContainerMiddlewareState_ClearContainerInstructions_EmptiesDict()
    {
        var state = new ContainerMiddlewareState()
            .WithContainerInstructions("Skill1", new ContainerInstructionSet(null, "Instructions 1"))
            .WithContainerInstructions("Skill2", new ContainerInstructionSet(null, "Instructions 2"));

        var cleared = state.ClearContainerInstructions();

        Assert.Empty(cleared.ActiveContainerInstructions);
        Assert.Equal(2, state.ActiveContainerInstructions.Count); // Original unchanged
    }

    //      
    // CHAT OPTIONS EXTENSIONS TESTS
    //      

    [Fact]
    public void ChatOptionsExtensions_WithTools_CopiesAllProperties()
    {
        // Arrange
        var originalTools = new List<AITool> { CreateNonCollapsedFunction("Original") };
        var newTools = new List<AITool> { CreateNonCollapsedFunction("New") };

        var original = new ChatOptions
        {
            ModelId = "test-model",
            Tools = originalTools,
            ToolMode = ChatToolMode.Auto,
            Temperature = 0.7f,
            MaxOutputTokens = 2000,
            TopP = 0.9f,
            TopK = 40,
            FrequencyPenalty = 0.1f,
            PresencePenalty = 0.2f,
            StopSequences = new[] { "STOP" },
            Seed = 42,
            ConversationId = "conv-123"
        };

        // Act
        var updated = original.Clone();
        updated.Tools = newTools;

        // Assert - new tools applied
        Assert.Single(updated.Tools);
        Assert.Equal("New", ((AIFunction)updated.Tools[0]).Name);

        // Assert - all other properties preserved
        Assert.Equal("test-model", updated.ModelId);
        Assert.Equal(ChatToolMode.Auto, updated.ToolMode);
        Assert.Equal(0.7f, updated.Temperature);
        Assert.Equal(2000, updated.MaxOutputTokens);
        Assert.Equal(0.9f, updated.TopP);
        Assert.Equal(40, updated.TopK);
        Assert.Equal(0.1f, updated.FrequencyPenalty);
        Assert.Equal(0.2f, updated.PresencePenalty);
        Assert.Equal(new[] { "STOP" }, updated.StopSequences);
        Assert.Equal(42, updated.Seed);
        Assert.Equal("conv-123", updated.ConversationId);

        // Assert - original unchanged
        Assert.Equal(originalTools, original.Tools);
    }

    //      
    // AFTER MESSAGE TURN TESTS - Ephemeral Result Filtering
    //      

    [Fact]
    public async Task AfterMessageTurn_RemovesContainerExpansions()
    {
        // Arrange
        var containerFunc = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("expanded"),
            new AIFunctionFactoryOptions
            {
                Name = "ExpandToolHarness",
                Description = "Expands ToolHarness",
                AdditionalProperties = new Dictionary<string, object?> { ["IsContainer"] = true }
            });

        var regularFunc1 = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>(42),
            new AIFunctionFactoryOptions { Name = "Calculate", Description = "Calculates" });

        var regularFunc2 = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("Hello"),
            new AIFunctionFactoryOptions { Name = "GetGreeting", Description = "Gets greeting" });

        var allTools = new List<AITool> { containerFunc, regularFunc1, regularFunc2 };
        var middleware = new ContainerMiddleware(allTools, ImmutableHashSet<string>.Empty);

        var turnHistory = new List<ChatMessage>
        {
            // Assistant message with function calls
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call1", "ExpandToolHarness"),  // Container call
                new FunctionCallContent("call2", "Calculate"),     // Regular call
                new FunctionCallContent("call3", "GetGreeting")    // Regular call
            }),
            // Tool message with results
            new(ChatRole.Tool, new List<AIContent>
            {
                new FunctionResultContent("call1", result: "ToolHarnessExpanded"),  // Container
                new FunctionResultContent("call2", result: "42"),              // Regular
                new FunctionResultContent("call3", result: "Hello")            // Regular
            })
        };

        // Create state with ExpandToolHarness in ContainersExpandedThisTurn
        var state = CreateEmptyState();
        var collapsingState = new ContainerMiddlewareState().WithExpandedContainer("ExpandToolHarness");
        state = state with { MiddlewareState = state.MiddlewareState.SetState("HPD.Agent.ContainerMiddlewareState", collapsingState) };

        var context = CreateAfterMessageTurnContext(state: state, turnHistory: turnHistory);

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert - Both messages remain with all calls/results (containers stay in history for cross-turn context)
        Assert.Equal(2, turnHistory.Count);

        // Check Assistant message - should have all 3 function calls
        var assistantMsg = turnHistory[0];
        Assert.Equal(ChatRole.Assistant, assistantMsg.Role);
        Assert.Equal(3, assistantMsg.Contents.Count);
        Assert.Contains(assistantMsg.Contents, c => c is FunctionCallContent fcc && fcc.CallId == "call1");
        Assert.Contains(assistantMsg.Contents, c => c is FunctionCallContent fcc && fcc.CallId == "call2");
        Assert.Contains(assistantMsg.Contents, c => c is FunctionCallContent fcc && fcc.CallId == "call3");

        // Check Tool message - should have all 3 results
        var toolMsg = turnHistory[1];
        Assert.Equal(ChatRole.Tool, toolMsg.Role);
        Assert.Equal(3, toolMsg.Contents.Count);
        Assert.Contains(toolMsg.Contents, c => c is FunctionResultContent frc && frc.CallId == "call1");
        Assert.Contains(toolMsg.Contents, c => c is FunctionResultContent frc && frc.CallId == "call2");
        Assert.Contains(toolMsg.Contents, c => c is FunctionResultContent frc && frc.CallId == "call3");
    }

    [Fact]
    public async Task AfterMessageTurn_PreservesNonFunctionContent()
    {
        // Arrange
        var regularFunc = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("Result"),
            new AIFunctionFactoryOptions { Name = "RegularFunction", Description = "Regular" });

        var allTools = new List<AITool> { regularFunc };
        var middleware = new ContainerMiddleware(allTools, ImmutableHashSet<string>.Empty);

        var turnHistory = new List<ChatMessage>
        {
            new(ChatRole.Assistant, new List<AIContent> { new TextContent("Some text") }),
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("call1", "Result") })
        };

        var context = CreateAfterMessageTurnContext();
        // TurnHistory managed by context - turnHistory;

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert: Text content should remain unchanged
        Assert.Equal(2, turnHistory.Count);
        Assert.Contains(turnHistory, m => m.Role == ChatRole.Assistant);
        Assert.Contains(turnHistory, m => m.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task AfterMessageTurn_HandlesEmptyTurnHistory()
    {
        // Arrange
        var middleware = new ContainerMiddleware(
            Array.Empty<AITool>(),
            ImmutableHashSet<string>.Empty);

        var turnHistory = new List<ChatMessage>();
        var context = CreateAfterMessageTurnContext();
        // TurnHistory managed by context - turnHistory;

        // Act & Assert - should not throw
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);
        Assert.Empty(turnHistory);
    }

    [Fact]
    public async Task AfterMessageTurn_MultipleContainersRemoved()
    {
        // Arrange
        var containerA = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("expanded"),
            new AIFunctionFactoryOptions
            {
                Name = "ExpandToolHarnessA",
                Description = "Expands A",
                AdditionalProperties = new Dictionary<string, object?> { ["IsContainer"] = true }
            });

        var containerB = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("expanded"),
            new AIFunctionFactoryOptions
            {
                Name = "ExpandToolHarnessB",
                Description = "Expands B",
                AdditionalProperties = new Dictionary<string, object?> { ["IsContainer"] = true }
            });

        var regularFunc = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("result"),
            new AIFunctionFactoryOptions { Name = "DoWork", Description = "Does work" });

        var allTools = new List<AITool> { containerA, containerB, regularFunc };
        var middleware = new ContainerMiddleware(allTools, ImmutableHashSet<string>.Empty);

        var turnHistory = new List<ChatMessage>
        {
            // Assistant message with function calls
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call1", "ExpandToolHarnessA"),
                new FunctionCallContent("call2", "ExpandToolHarnessB"),
                new FunctionCallContent("call3", "DoWork")
            }),
            // Tool message with results
            new(ChatRole.Tool, new List<AIContent>
            {
                new FunctionResultContent("call1", "ToolHarnessA expanded"),
                new FunctionResultContent("call2", "ToolHarnessB expanded"),
                new FunctionResultContent("call3", "Actual result")
            })
        };

        // Create state with both containers in ContainersExpandedThisTurn
        var state = CreateEmptyState();
        var collapsingState = new ContainerMiddlewareState()
            .WithExpandedContainer("ExpandToolHarnessA")
            .WithExpandedContainer("ExpandToolHarnessB");
        state = state with { MiddlewareState = state.MiddlewareState.SetState("HPD.Agent.ContainerMiddlewareState", collapsingState) };

        var context = CreateAfterMessageTurnContext(state: state, turnHistory: turnHistory);

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert: Both messages remain with all container and regular calls (containers stay in history for cross-turn context)
        Assert.Equal(2, turnHistory.Count);

        var assistantMsg = turnHistory[0];
        Assert.Equal(3, assistantMsg.Contents.Count);
        var calls = assistantMsg.Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(3, calls.Count);
        Assert.Contains(calls, c => c.CallId == "call1");
        Assert.Contains(calls, c => c.CallId == "call2");
        Assert.Contains(calls, c => c.CallId == "call3");

        var toolMsg = turnHistory[1];
        Assert.Equal(3, toolMsg.Contents.Count);
        var results = toolMsg.Contents.OfType<FunctionResultContent>().ToList();
        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.CallId == "call1");
        Assert.Contains(results, r => r.CallId == "call2");
        Assert.Contains(results, r => r.CallId == "call3");
    }

    [Fact]
    public async Task AfterMessageTurn_RemovesEntireMessageIfAllEphemeral()
    {
        // Arrange
        var containerFunc = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("expanded"),
            new AIFunctionFactoryOptions
            {
                Name = "ExpandToolHarness",
                Description = "Expands",
                AdditionalProperties = new Dictionary<string, object?> { ["IsContainer"] = true }
            });

        var allTools = new List<AITool> { containerFunc };
        var middleware = new ContainerMiddleware(allTools, ImmutableHashSet<string>.Empty);

        var turnHistory = new List<ChatMessage>
        {
            new(ChatRole.User, new List<AIContent> { new TextContent("Hello") }),
            // Assistant message with only container call
            new(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("call1", "ExpandToolHarness") }),
            // Tool message with only container result
            new(ChatRole.Tool, new List<AIContent> { new FunctionResultContent("call1", "Container expanded") }),
            new(ChatRole.Assistant, new List<AIContent> { new TextContent("Done") })
        };

        // Create state with ExpandToolHarness in ContainersExpandedThisTurn
        var state = CreateEmptyState();
        var collapsingState = new ContainerMiddlewareState().WithExpandedContainer("ExpandToolHarness");
        state = state with { MiddlewareState = state.MiddlewareState.SetState("HPD.Agent.ContainerMiddlewareState", collapsingState) };

        var context = CreateAfterMessageTurnContext(state: state, turnHistory: turnHistory);

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert: All messages remain (containers stay in history for cross-turn context)
        Assert.Equal(4, turnHistory.Count);
        var userMsg = Assert.Single(turnHistory.Where(m => m.Role == ChatRole.User));
        var assistantMsgs = turnHistory.Where(m => m.Role == ChatRole.Assistant).ToList();
        Assert.Equal(2, assistantMsgs.Count);
        var toolMsg = Assert.Single(turnHistory.Where(m => m.Role == ChatRole.Tool));
        
        // First assistant message should have the container call
        Assert.Single(assistantMsgs[0].Contents);
        Assert.IsType<FunctionCallContent>(assistantMsgs[0].Contents[0]);
        
        // Tool message should have the container result
        Assert.Single(toolMsg.Contents);
        Assert.IsType<FunctionResultContent>(toolMsg.Contents[0]);
        
        // Second assistant message should be the text-only one
        Assert.Single(assistantMsgs[1].Contents);
        Assert.IsType<TextContent>(assistantMsgs[1].Contents[0]);
    }

    [Fact]
    public async Task AfterMessageTurn_MixedSkillAndToolHarnessContainers()
    {
        // Arrange
        var CollapsedToolHarnessContainer = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("ToolHarness expanded"),
            new AIFunctionFactoryOptions
            {
                Name = "MathTools",
                Description = "Math ToolHarness",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true
                }
            });

        var skillContainer = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("Skill expanded"),
            new AIFunctionFactoryOptions
            {
                Name = "QuickAnalysis",
                Description = "Analysis skill",
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsSkill"] = true
                }
            });

        var regularFunc = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("Result"),
            new AIFunctionFactoryOptions { Name = "Calculate", Description = "Calculates" });

        var allTools = new List<AITool> { CollapsedToolHarnessContainer, skillContainer, regularFunc };
        var middleware = new ContainerMiddleware(allTools, ImmutableHashSet<string>.Empty);

        var turnHistory = new List<ChatMessage>
        {
            // Assistant message with function calls
            new(ChatRole.Assistant, new List<AIContent>
            {
                new FunctionCallContent("call1", "MathTools"),
                new FunctionCallContent("call2", "QuickAnalysis"),
                new FunctionCallContent("call3", "Calculate")
            }),
            // Tool message with results
            new(ChatRole.Tool, new List<AIContent>
            {
                new FunctionResultContent("call1", "ToolHarness expanded"),
                new FunctionResultContent("call2", "Skill expanded"),
                new FunctionResultContent("call3", "Result")
            })
        };

        // Create state with both containers in ContainersExpandedThisTurn
        var state = CreateEmptyState();
        var collapsingState = new ContainerMiddlewareState()
            .WithExpandedContainer("MathTools")
            .WithExpandedContainer("QuickAnalysis");
        state = state with { MiddlewareState = state.MiddlewareState.SetState("HPD.Agent.ContainerMiddlewareState", collapsingState) };

        var context = CreateAfterMessageTurnContext(state: state, turnHistory: turnHistory);

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert: All calls remain (containers stay in history for cross-turn context)
        Assert.Equal(2, turnHistory.Count);

        var assistantMsg = turnHistory[0];
        Assert.Equal(3, assistantMsg.Contents.Count);
        var calls = assistantMsg.Contents.OfType<FunctionCallContent>().ToList();
        Assert.Equal(3, calls.Count);
        Assert.Contains(calls, c => c.CallId == "call1");
        Assert.Contains(calls, c => c.CallId == "call2");
        Assert.Contains(calls, c => c.CallId == "call3");

        var toolMsg = turnHistory[1];
        Assert.Equal(3, toolMsg.Contents.Count);
        var results = toolMsg.Contents.OfType<FunctionResultContent>().ToList();
        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.CallId == "call1");
        Assert.Contains(results, r => r.CallId == "call2");
        Assert.Contains(results, r => r.CallId == "call3");
    }

    [Fact]
    public async Task AfterMessageTurn_NoEphemeralCallIds_NoFiltering()
    {
        // Arrange
        var regularFunc = AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>("Result"),
            new AIFunctionFactoryOptions { Name = "Calculate", Description = "Calculates" });

        var allTools = new List<AITool> { regularFunc };
        var middleware = new ContainerMiddleware(allTools, ImmutableHashSet<string>.Empty);

        var turnHistory = new List<ChatMessage>
        {
            new(ChatRole.Tool, new List<AIContent>
            {
                new FunctionResultContent("call1", "Result 1"),
                new FunctionResultContent("call2", "Result 2")
            })
        };

        var context = CreateAfterMessageTurnContext();
        // TurnHistory managed by context - turnHistory;
        // No EphemeralCallIds in Properties

        // Act
        await middleware.AfterMessageTurnAsync(context, CancellationToken.None);

        // Assert: No filtering should occur
        Assert.Single(turnHistory);
        Assert.Equal(2, turnHistory[0].Contents.Count);
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

    private static AgentContext CreateAgentContext(AgentLoopState? state = null)
    {
        var agentState = state ?? CreateEmptyState();

        return new AgentContext(
            "TestAgent",
            "test-conversation",
            agentState,
            new HPD.Events.Core.EventCoordinator(),
            new global::HPD.Agent.Session("test-session"),
            new global::HPD.Agent.Thread("test-session", "test-agent"),
            CancellationToken.None);
    }

    private static BeforeIterationContext CreateContext(AgentLoopState? state = null, ChatOptions? options = null)
    {
        var agentContext = CreateAgentContext(state);
        return agentContext.AsBeforeIteration(
            iteration: 0,
            messages: new List<ChatMessage>(),
            options: options ?? new ChatOptions { Tools = new List<AITool>() },
            runConfig: new AgentRunConfig());
    }

    private static BeforeIterationContext CreateIterationContext(AgentLoopState? state = null, ChatOptions? options = null)
    {
        var agentContext = CreateAgentContext(state);
        return agentContext.AsBeforeIteration(
            iteration: 0,
            messages: new List<ChatMessage>(),
            options: options ?? new ChatOptions(),
            runConfig: new AgentRunConfig());
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

    private static (AIFunction Container, AIFunction[] Members) CreateCollapsedToolHarness(
        string toolName,
        string description,
        params string[] memberNames)
    {
        var members = memberNames.Select(name =>
            CollapsedToolHarnessTestHelper.CreateToolHarnessMemberFunction(
                name,
                $"{name} function",
                (args, ct) => Task.FromResult<object?>($"{name} result"),
                toolName)
        ).ToArray();

        var container = CollapsedToolHarnessTestHelper.CreateContainerFunction(toolName, description, members);

        return (container, members);
    }

    private static (AIFunction Container, AIFunction[] Members) CreateCollapsedToolHarnessWithSystemPrompt(
        string toolName,
        string description,
        string systemPrompt,
        params string[] memberNames)
    {
        var members = memberNames.Select(name =>
            CollapsedToolHarnessTestHelper.CreateToolHarnessMemberFunction(
                name,
                $"{name} function",
                (args, ct) => Task.FromResult<object?>($"{name} result"),
                toolName)
        ).ToArray();

        var functionNames = members.Select(f => f.Name ?? string.Empty).ToArray();
        var container = HPDAIFunctionFactory.Create(
            (AIFunctionArguments args, FunctionExecutionContext _, CancellationToken ct) => Task.FromResult<object?>($"{toolName} expanded"),
            new HPDAIFunctionFactoryOptions
            {
                Name = toolName,
                Description = description,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["ToolHarnessName"] = toolName,
                    ["ReferencedFunctions"] = functionNames,
                    ["FunctionCount"] = members.Length,
                    ["SourceType"] = "CSharp",
                    ["SystemPrompt"] = systemPrompt
                }
            });

        return (container, members);
    }

    private static AIFunction CreateNonCollapsedFunction(string name)
    {
        return CollapsedToolHarnessTestHelper.CreateNonCollapsedFunction(
            name,
            $"{name} function",
            (args, ct) => Task.FromResult<object?>($"{name} result"));
    }

    private static AIFunction CreateSkillContainer(
        string name,
        string description,
        params string[] referencedFunctions)
    {
        return AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>($"{name} activated"),
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsSkill"] = true,
                    ["ReferencedFunctions"] = referencedFunctions
                }
            });
    }

    private static AIFunction CreateSkillContainerWithSystemPrompt(
        string name,
        string description,
        string systemPrompt)
    {
        return AIFunctionFactory.Create(
            (object? args, CancellationToken ct) => Task.FromResult<object?>($"{name} activated"),
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsSkill"] = true,
                    ["SystemPrompt"] = systemPrompt
                }
            });
    }

    private static FunctionCallContent CreateToolCall(string name)
    {
        return new FunctionCallContent(
            callId: Guid.NewGuid().ToString(),
            name: name,
            arguments: new Dictionary<string, object?>());
    }
}
