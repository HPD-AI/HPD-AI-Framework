using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Events.Core;
using Microsoft.Extensions.AI;
using System.Collections.Immutable;
using Xunit;

namespace HPD.Agent.Tests.Middleware;

/// <summary>
/// Stress tests for container instruction lifecycle across multiple message turns and scenarios.
/// Tests the critical requirement: instructions must persist within a turn but be cleared between turns.
/// </summary>
public class ContainerInstructionLifecycleStressTests
{
    #region Cross-Turn Instruction Clearing Tests

    [Fact]
    public async Task Instructions_ClearedBetweenTurns_SingleContainer()
    {
        // Scenario: Turn 1 expands CodingToolkit, Turn 2 should have clean prompt
        var middleware = CreateContainerMiddleware();

        // Turn 1: Expand CodingToolkit
        var turn1State = new ContainerMiddlewareState()
            .WithExpandedContainer("CodingToolkit")
            .WithContainerInstructions("CodingToolkit", new ContainerInstructionSet(
                FunctionResult: "CodingToolkit expanded",
                SystemPrompt: "CODING RULES: Always validate paths"));

        var turn1Context = CreateBeforeIterationContext(turn1State, iteration: 0);
        await middleware.BeforeIterationAsync(turn1Context, CancellationToken.None);

        // Verify instructions injected in Turn 1
        Assert.Contains(" ACTIVE CONTAINER PROTOCOLS", turn1Context.Options!.Instructions!);
        Assert.Contains("CODING RULES", turn1Context.Options.Instructions);

        // Simulate end of Turn 1
        var afterTurn1Context = CreateAfterMessageTurnContext(turn1State);
        await middleware.AfterMessageTurnAsync(afterTurn1Context, CancellationToken.None);

        // Get updated state after turn cleanup
        var turn2State = afterTurn1Context.GetMiddlewareState<ContainerMiddlewareState>() ?? new ContainerMiddlewareState();

        // Turn 2: New message turn with cleared state
        var turn2Context = CreateBeforeIterationContext(turn2State, iteration: 0);
        turn2Context.Options!.Instructions = "You are a helpful AI assistant."; // Simulate fresh ChatOptions
        await middleware.BeforeIterationAsync(turn2Context, CancellationToken.None);

        // Verify instructions NOT present in Turn 2
        Assert.DoesNotContain(" ACTIVE CONTAINER PROTOCOLS", turn2Context.Options.Instructions);
        Assert.DoesNotContain("CODING RULES", turn2Context.Options.Instructions);
        Assert.Equal("You are a helpful AI assistant.", turn2Context.Options.Instructions);
    }

    [Fact]
    public async Task Instructions_ClearedBetweenTurns_MultipleContainers()
    {
        // Scenario: Turn 1 expands multiple containers, Turn 2 should have clean prompt
        var middleware = CreateContainerMiddleware();

        // Turn 1: Expand multiple containers
        var turn1State = new ContainerMiddlewareState()
            .WithExpandedContainer("CodingToolkit")
            .WithExpandedContainer("MathToolkit")
            .WithContainerInstructions("CodingToolkit", new ContainerInstructionSet(
                FunctionResult: null,
                SystemPrompt: "CODING: Validate paths"))
            .WithContainerInstructions("MathToolkit", new ContainerInstructionSet(
                FunctionResult: null,
                SystemPrompt: "MATH: Always round to 2 decimals"));

        var turn1Context = CreateBeforeIterationContext(turn1State, iteration: 1);
        await middleware.BeforeIterationAsync(turn1Context, CancellationToken.None);

        // Verify both container instructions injected
        Assert.Contains("CODING: Validate paths", turn1Context.Options!.Instructions!);
        Assert.Contains("MATH: Always round to 2 decimals", turn1Context.Options.Instructions);

        // Simulate end of Turn 1
        var afterTurn1Context = CreateAfterMessageTurnContext(turn1State);
        await middleware.AfterMessageTurnAsync(afterTurn1Context, CancellationToken.None);
        var turn2State = afterTurn1Context.GetMiddlewareState<ContainerMiddlewareState>() ?? new ContainerMiddlewareState();

        // Turn 2: New message turn
        var turn2Context = CreateBeforeIterationContext(turn2State, iteration: 0);
        turn2Context.Options!.Instructions = "You are a helpful AI assistant.";
        await middleware.BeforeIterationAsync(turn2Context, CancellationToken.None);

        // Verify ALL container instructions cleared
        Assert.DoesNotContain("CODING: Validate paths", turn2Context.Options.Instructions);
        Assert.DoesNotContain("MATH: Always round to 2 decimals", turn2Context.Options.Instructions);
    }

    [Fact]
    public async Task Instructions_RemovedFromStaleOptions_WhenStateEmpty()
    {
        // Scenario: ChatOptions has stale instructions but state is empty (simulates session restoration)
        var middleware = CreateContainerMiddleware();

        var emptyState = new ContainerMiddlewareState(); // No active containers
        var context = CreateBeforeIterationContext(emptyState, iteration: 0);

        // Simulate stale ChatOptions with old protocols embedded
        context.Options!.Instructions = """
            You are a helpful AI assistant.

            ═════════════════════════════════════════════════════════════════════════════════════════════════
             ACTIVE CONTAINER PROTOCOLS (Execute ALL steps completely)
            ═════════════════════════════════════════════════════════════════════════════════════════════════

            ## CodingToolkit:

            STALE INSTRUCTIONS: This should be removed
            """;

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Verify stale protocols were removed
        Assert.DoesNotContain(" ACTIVE CONTAINER PROTOCOLS", context.Options.Instructions);
        Assert.DoesNotContain("STALE INSTRUCTIONS", context.Options.Instructions);
        Assert.Equal("You are a helpful AI assistant.", context.Options.Instructions.Trim());
    }

    #endregion

    #region Within-Turn Instruction Persistence Tests

    [Fact]
    public async Task Instructions_PersistWithinTurn_AcrossIterations()
    {
        // Scenario: Container expanded in iteration 1, instructions should persist through iterations 2, 3, etc.
        var middleware = CreateContainerMiddleware();

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("MathToolkit")
            .WithContainerInstructions("MathToolkit", new ContainerInstructionSet(
                FunctionResult: null,
                SystemPrompt: "MATH RULES: Use SI units"));

        // Iteration 1
        var iter1Context = CreateBeforeIterationContext(state, iteration: 1);
        await middleware.BeforeIterationAsync(iter1Context, CancellationToken.None);
        Assert.Contains("MATH RULES", iter1Context.Options!.Instructions!);

        // Iteration 2 (same turn, same state)
        var iter2Context = CreateBeforeIterationContext(state, iteration: 2);
        iter2Context.Options!.Instructions = iter1Context.Options.Instructions; // Simulate ChatOptions reuse
        await middleware.BeforeIterationAsync(iter2Context, CancellationToken.None);
        Assert.Contains("MATH RULES", iter2Context.Options.Instructions);

        // Iteration 3 (same turn, same state)
        var iter3Context = CreateBeforeIterationContext(state, iteration: 3);
        iter3Context.Options!.Instructions = iter2Context.Options.Instructions;
        await middleware.BeforeIterationAsync(iter3Context, CancellationToken.None);
        Assert.Contains("MATH RULES", iter3Context.Options.Instructions);
    }

    [Fact]
    public async Task Instructions_UpdatedWhenContainerAddedMidTurn()
    {
        // Scenario: Iteration 1 has ContainerA, Iteration 2 expands ContainerB, both should be present
        var middleware = CreateContainerMiddleware();

        // Iteration 1: Only ContainerA
        var iter1State = new ContainerMiddlewareState()
            .WithExpandedContainer("ContainerA")
            .WithContainerInstructions("ContainerA", new ContainerInstructionSet(
                FunctionResult: null,
                SystemPrompt: "RULES A"));

        var iter1Context = CreateBeforeIterationContext(iter1State, iteration: 1);
        await middleware.BeforeIterationAsync(iter1Context, CancellationToken.None);
        Assert.Contains("RULES A", iter1Context.Options!.Instructions!);
        Assert.DoesNotContain("RULES B", iter1Context.Options.Instructions);

        // Iteration 2: ContainerB added
        var iter2State = iter1State
            .WithExpandedContainer("ContainerB")
            .WithContainerInstructions("ContainerB", new ContainerInstructionSet(
                FunctionResult: null,
                SystemPrompt: "RULES B"));

        var iter2Context = CreateBeforeIterationContext(iter2State, iteration: 2);
        iter2Context.Options!.Instructions = iter1Context.Options.Instructions; // Carry forward previous instructions
        await middleware.BeforeIterationAsync(iter2Context, CancellationToken.None);

        // Both should be present
        Assert.Contains("RULES A", iter2Context.Options.Instructions);
        Assert.Contains("RULES B", iter2Context.Options.Instructions);
    }

    #endregion

    #region Multiple Containers Same Turn Tests

    [Fact]
    public async Task MultipleContainers_AllInstructionsInjected_WhenExpandedSameTurn()
    {
        // Scenario: 3 containers expanded in same turn, all instructions should appear
        var middleware = CreateContainerMiddleware();

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("Container1")
            .WithExpandedContainer("Container2")
            .WithExpandedContainer("Container3")
            .WithContainerInstructions("Container1", new ContainerInstructionSet(
                FunctionResult: null,
                SystemPrompt: "RULE 1: First container"))
            .WithContainerInstructions("Container2", new ContainerInstructionSet(
                FunctionResult: null,
                SystemPrompt: "RULE 2: Second container"))
            .WithContainerInstructions("Container3", new ContainerInstructionSet(
                FunctionResult: null,
                SystemPrompt: "RULE 3: Third container"));

        var context = CreateBeforeIterationContext(state, iteration: 1);
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // All three should be in instructions
        Assert.Contains("Container1", context.Options!.Instructions!);
        Assert.Contains("RULE 1: First container", context.Options.Instructions);
        Assert.Contains("Container2", context.Options.Instructions);
        Assert.Contains("RULE 2: Second container", context.Options.Instructions);
        Assert.Contains("Container3", context.Options.Instructions);
        Assert.Contains("RULE 3: Third container", context.Options.Instructions);
    }

    [Fact]
    public async Task MultipleContainers_InstructionsAlphabeticallySorted()
    {
        // Verify containers are sorted alphabetically for consistency
        var middleware = CreateContainerMiddleware();

        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("ZebraToolkit")
            .WithExpandedContainer("AlphaToolkit")
            .WithExpandedContainer("MidToolkit")
            .WithContainerInstructions("ZebraToolkit", new ContainerInstructionSet(null, "Zebra rules"))
            .WithContainerInstructions("AlphaToolkit", new ContainerInstructionSet(null, "Alpha rules"))
            .WithContainerInstructions("MidToolkit", new ContainerInstructionSet(null, "Mid rules"));

        var context = CreateBeforeIterationContext(state, iteration: 1);
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        var instructions = context.Options!.Instructions!;
        var alphaIndex = instructions.IndexOf("## AlphaToolkit:");
        var midIndex = instructions.IndexOf("## MidToolkit:");
        var zebraIndex = instructions.IndexOf("## ZebraToolkit:");

        // Verify alphabetical order
        Assert.True(alphaIndex < midIndex, "AlphaToolkit should appear before MidToolkit");
        Assert.True(midIndex < zebraIndex, "MidToolkit should appear before ZebraToolkit");
    }

    #endregion

    #region Sequential Turn Tests

    [Fact]
    public async Task ThreeTurnsSequential_InstructionsClearedBetweenEach()
    {
        // Scenario: Turn 1 → CodingToolkit, Turn 2 → MathToolkit, Turn 3 → Both cleared
        var middleware = CreateContainerMiddleware();

        // Turn 1: CodingToolkit
        var turn1State = new ContainerMiddlewareState()
            .WithExpandedContainer("CodingToolkit")
            .WithContainerInstructions("CodingToolkit", new ContainerInstructionSet(null, "CODING INSTRUCTIONS"));

        var turn1Context = CreateBeforeIterationContext(turn1State, iteration: 0);
        await middleware.BeforeIterationAsync(turn1Context, CancellationToken.None);
        Assert.Contains("CODING INSTRUCTIONS", turn1Context.Options!.Instructions!);

        var afterTurn1 = CreateAfterMessageTurnContext(turn1State);
        await middleware.AfterMessageTurnAsync(afterTurn1, CancellationToken.None);

        // Turn 2: MathToolkit (CodingToolkit cleared)
        var turn2State = new ContainerMiddlewareState() // Fresh state
            .WithExpandedContainer("MathToolkit")
            .WithContainerInstructions("MathToolkit", new ContainerInstructionSet(null, "MATH INSTRUCTIONS"));

        var turn2Context = CreateBeforeIterationContext(turn2State, iteration: 0);
        turn2Context.Options!.Instructions = "You are a helpful AI assistant."; // Fresh options
        await middleware.BeforeIterationAsync(turn2Context, CancellationToken.None);
        Assert.Contains("MATH INSTRUCTIONS", turn2Context.Options.Instructions);
        Assert.DoesNotContain("CODING INSTRUCTIONS", turn2Context.Options.Instructions); // Turn 1 cleared

        var afterTurn2 = CreateAfterMessageTurnContext(turn2State);
        await middleware.AfterMessageTurnAsync(afterTurn2, CancellationToken.None);

        // Turn 3: No containers (both cleared)
        var turn3State = new ContainerMiddlewareState(); // Empty
        var turn3Context = CreateBeforeIterationContext(turn3State, iteration: 0);
        turn3Context.Options!.Instructions = "You are a helpful AI assistant.";
        await middleware.BeforeIterationAsync(turn3Context, CancellationToken.None);
        Assert.DoesNotContain("CODING INSTRUCTIONS", turn3Context.Options.Instructions);
        Assert.DoesNotContain("MATH INSTRUCTIONS", turn3Context.Options.Instructions);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task EmptyState_NoInstructionsInjected()
    {
        var middleware = CreateContainerMiddleware();
        var emptyState = new ContainerMiddlewareState();
        var context = CreateBeforeIterationContext(emptyState, iteration: 0);
        var originalInstructions = "Original instructions";
        context.Options!.Instructions = originalInstructions;

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        Assert.Equal(originalInstructions, context.Options.Instructions);
        Assert.DoesNotContain(" ACTIVE", context.Options.Instructions);
    }

    [Fact]
    public async Task NullSystemPrompt_OnlyHeader_NoContainerContent()
    {
        var middleware = CreateContainerMiddleware();
        var state = new ContainerMiddlewareState()
            .WithExpandedContainer("TestToolkit")
            .WithContainerInstructions("TestToolkit", new ContainerInstructionSet(
                FunctionResult: "Some result",
                SystemPrompt: null)); // Null system prompt

        var context = CreateBeforeIterationContext(state, iteration: 0);
        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Header present but no container content since SystemPrompt is null
        Assert.Contains(" ACTIVE CONTAINER PROTOCOLS", context.Options!.Instructions!);
        Assert.DoesNotContain("TestToolkit", context.Options.Instructions);
    }

    [Fact]
    public async Task ProtocolRemoval_HandlesPartialMarker()
    {
        // Edge case: Stale instructions with only partial marker
        var middleware = CreateContainerMiddleware();
        var emptyState = new ContainerMiddlewareState();
        var context = CreateBeforeIterationContext(emptyState, iteration: 0);

        // Only the emoji, not full marker
        context.Options!.Instructions = "Instructions\n ACTIVE but incomplete marker";

        await middleware.BeforeIterationAsync(context, CancellationToken.None);

        // Should NOT remove since marker is incomplete
        Assert.Contains(" ACTIVE but incomplete marker", context.Options.Instructions);
    }

    #endregion

    #region Test Helpers

    private static ContainerMiddleware CreateContainerMiddleware()
    {
        var tools = new List<AITool>();
        var explicitGroups = ImmutableHashSet<string>.Empty;
        var config = new CollapsingConfig { Enabled = true };
        return new ContainerMiddleware(tools, explicitGroups, null, null, null, config, logger: null);
    }

    private static BeforeIterationContext CreateBeforeIterationContext(
        ContainerMiddlewareState state,
        int iteration)
    {
        var messages = new List<ChatMessage>();
        var options = new ChatOptions
        {
            Tools = new List<AITool>(),
            Instructions = "You are a helpful AI assistant."
        };
        var runConfig = new AgentRunConfig();

        var loopState = AgentLoopState.InitialSafe(
            messages: messages,
            runId: "test-run-id",
            conversationId: "test-conversation",
            agentName: "TestAgent")
            with
            {
                MiddlewareState = new MiddlewareState().SetState(
                    "HPD.Agent.ContainerMiddlewareState",
                    state)
            };

        var agentContext = new AgentContext(
            agentName: "TestAgent",
            conversationId: "test-conversation",
            initialState: loopState,
            eventCoordinator: new EventCoordinator(),
            session: new global::HPD.Agent.Session("test-session"),
            branch: new global::HPD.Agent.Branch("test-session"),
            cancellationToken: CancellationToken.None);

        return agentContext.AsBeforeIteration(iteration, messages, options, runConfig);
    }

    private static AfterMessageTurnContext CreateAfterMessageTurnContext(ContainerMiddlewareState state)
    {
        var messages = new List<ChatMessage>();
        var loopState = AgentLoopState.InitialSafe(
            messages: messages,
            runId: "test-run-id",
            conversationId: "test-conversation",
            agentName: "TestAgent")
            with
            {
                MiddlewareState = new MiddlewareState().SetState(
                    "HPD.Agent.ContainerMiddlewareState",
                    state)
            };

        var agentContext = new AgentContext(
            agentName: "TestAgent",
            conversationId: "test-conversation",
            initialState: loopState,
            eventCoordinator: new EventCoordinator(),
            session: new global::HPD.Agent.Session("test-session"),
            branch: new global::HPD.Agent.Branch("test-session"),
            cancellationToken: CancellationToken.None);

        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Response"));
        var turnHistory = new List<ChatMessage>();
        var runConfig = new AgentRunConfig();

        return agentContext.AsAfterMessageTurn(response, turnHistory, runConfig);
    }

    #endregion
}
