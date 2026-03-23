using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Planning;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Planning;

/// <summary>
/// Tests for the Plan Mode system:
/// - PlanModePersistentStateData (immutable state model)
/// - AgentPlanData / PlanStepData (plan domain model)
/// - AgentPlanToolkit (agent-callable functions)
/// - AgentPlanAgentMiddleware (context injection)
/// - PlanModeBuilderExtensions (builder integration)
/// </summary>
public class PlanModeTests
{
    private const string ConvId = "conv-001";

    // ═══════════════════════════════════════════════════════════════════
    // P-1 through P-4: PlanModePersistentStateData immutable model
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreatePlan_ProducesCorrectStructure()
    {
        var plan = PlanModePersistentStateData.CreatePlan("Fix the authentication bug",
            ["Reproduce the bug", "Find root cause", "Write fix", "Run tests"]);

        Assert.Equal("Fix the authentication bug", plan.Goal);
        Assert.Equal(4, plan.Steps.Count);
        Assert.Equal("1", plan.Steps[0].Id);
        Assert.Equal("Reproduce the bug", plan.Steps[0].Description);
        Assert.Equal(PlanStepStatus.Pending, plan.Steps[0].Status);
        Assert.False(plan.IsComplete);
    }

    [Fact]
    public void WithPlan_StoresPlanForConversation()
    {
        var state = new PlanModePersistentStateData();
        var plan = PlanModePersistentStateData.CreatePlan("goal", ["step1"]);

        var updated = state.WithPlan(ConvId, plan);

        Assert.Null(state.GetPlan(ConvId)); // original unchanged
        Assert.NotNull(updated.GetPlan(ConvId));
        Assert.Equal("goal", updated.GetPlan(ConvId)!.Goal);
    }

    [Fact]
    public void WithoutPlan_RemovesPlan()
    {
        var state = new PlanModePersistentStateData();
        var plan = PlanModePersistentStateData.CreatePlan("goal", ["step"]);
        var withPlan = state.WithPlan(ConvId, plan);

        var withoutPlan = withPlan.WithoutPlan(ConvId);

        Assert.Null(withoutPlan.GetPlan(ConvId));
        Assert.NotNull(withPlan.GetPlan(ConvId)); // original still has it
    }

    [Fact]
    public void HasActivePlan_FalseAfterPlanCompleted()
    {
        var plan = PlanModePersistentStateData.CreatePlan("goal", ["step1"]);
        var state = new PlanModePersistentStateData().WithPlan(ConvId, plan);

        Assert.True(state.HasActivePlan(ConvId));

        var completed = plan.AsCompleted();
        var stateWithCompleted = state.WithPlan(ConvId, completed);

        Assert.False(stateWithCompleted.HasActivePlan(ConvId));
    }

    // ═══════════════════════════════════════════════════════════════════
    // P-5 through P-10: AgentPlanData domain model mutations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WithUpdatedStep_ChangesStatusAndNotes()
    {
        var plan = PlanModePersistentStateData.CreatePlan("goal",
            ["step 1", "step 2", "step 3"]);

        var updated = plan.WithUpdatedStep("2", PlanStepStatus.InProgress, "Working on it");

        Assert.Equal(PlanStepStatus.Pending, plan.Steps[1].Status); // original unchanged
        Assert.Equal(PlanStepStatus.InProgress, updated.Steps[1].Status);
        Assert.Equal("Working on it", updated.Steps[1].Notes);
        // Other steps unchanged
        Assert.Equal(PlanStepStatus.Pending, updated.Steps[0].Status);
        Assert.Equal(PlanStepStatus.Pending, updated.Steps[2].Status);
    }

    [Fact]
    public void WithUpdatedStep_UnknownId_ReturnsOriginal()
    {
        var plan = PlanModePersistentStateData.CreatePlan("goal", ["step 1"]);

        var result = plan.WithUpdatedStep("99", PlanStepStatus.Completed);

        Assert.Same(plan, result);
    }

    [Fact]
    public void WithAddedStep_AppendsToEnd()
    {
        var plan = PlanModePersistentStateData.CreatePlan("goal", ["step 1", "step 2"]);

        var updated = plan.WithAddedStep("step 3 — new");

        Assert.Equal(3, updated.Steps.Count);
        Assert.Equal("step 3 — new", updated.Steps[2].Description);
        Assert.Equal(PlanStepStatus.Pending, updated.Steps[2].Status);
    }

    [Fact]
    public void WithAddedStep_AfterStepId_InsertsAtCorrectPosition()
    {
        var plan = PlanModePersistentStateData.CreatePlan("goal",
            ["step 1", "step 2", "step 3"]);

        var updated = plan.WithAddedStep("inserted after step 1", afterStepId: "1");

        Assert.Equal(4, updated.Steps.Count);
        Assert.Equal("step 1", updated.Steps[0].Description);
        Assert.Equal("inserted after step 1", updated.Steps[1].Description);
        Assert.Equal("step 2", updated.Steps[2].Description);
    }

    [Fact]
    public void WithContextNote_AppendsTimestampedNote()
    {
        var plan = PlanModePersistentStateData.CreatePlan("goal", ["step"]);

        var updated = plan.WithContextNote("Found the root cause");

        Assert.Single(updated.ContextNotes);
        Assert.Contains("Found the root cause", updated.ContextNotes[0]);
        // Should include a timestamp prefix
        Assert.Contains(":", updated.ContextNotes[0]); // HH:mm:ss format
        Assert.Empty(plan.ContextNotes); // original unchanged
    }

    [Fact]
    public void AsCompleted_SetsIsCompleteAndCompletedAt()
    {
        var plan = PlanModePersistentStateData.CreatePlan("goal", ["step"]);
        var before = DateTime.UtcNow;

        var completed = plan.AsCompleted();

        var after = DateTime.UtcNow;
        Assert.True(completed.IsComplete);
        Assert.NotNull(completed.CompletedAt);
        Assert.True(completed.CompletedAt >= before);
        Assert.True(completed.CompletedAt <= after);
        Assert.False(plan.IsComplete); // original unchanged
    }

    // ═══════════════════════════════════════════════════════════════════
    // P-11: BuildPlanPrompt produces well-formed output
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPlanPrompt_IncludesGoalStepsAndNotes()
    {
        var plan = PlanModePersistentStateData.CreatePlan("Fix auth bug",
            ["Reproduce", "Root cause", "Fix"]);

        var withNote = plan.WithContextNote("Auth uses JWT not sessions");
        var withProgress = withNote
            .WithUpdatedStep("1", PlanStepStatus.Completed)
            .WithUpdatedStep("2", PlanStepStatus.InProgress, "Checking middleware");

        var prompt = withProgress.BuildPlanPrompt();

        Assert.Contains("[CURRENT_PLAN]", prompt);
        Assert.Contains("Fix auth bug", prompt);
        Assert.Contains("[1]", prompt);
        Assert.Contains("[2]", prompt);
        Assert.Contains("[3]", prompt);
        Assert.Contains("Completed", prompt);
        Assert.Contains("InProgress", prompt);
        Assert.Contains("Auth uses JWT not sessions", prompt);
        Assert.Contains("[END_CURRENT_PLAN]", prompt);
    }

    [Fact]
    public void BuildPlanPrompt_CompletedPlan_ShowsCompletedStatus()
    {
        var plan = PlanModePersistentStateData
            .CreatePlan("Done goal", ["step 1"])
            .AsCompleted();

        var prompt = plan.BuildPlanPrompt();

        Assert.Contains("COMPLETED", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P-12 through P-15: AgentPlanAgentMiddleware context injection
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AgentPlanMiddleware_NoPlan_NoInjection()
    {
        var middleware = new AgentPlanAgentMiddleware();
        var context = CreateContext();

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // No plan in state → nothing injected
        Assert.DoesNotContain(context.ConversationHistory,
            m => m.Text?.Contains("[CURRENT_PLAN]") == true);
    }

    [Fact]
    public async Task AgentPlanMiddleware_WithActivePlan_InjectsPlanAsSystemMessage()
    {
        var plan = PlanModePersistentStateData.CreatePlan("Write the report", ["Draft", "Review"]);
        var planState = new PlanModePersistentStateData().WithPlan(ConvId, plan);
        var middleware = new AgentPlanAgentMiddleware();
        var context = CreateContextWithPlanState(planState);

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Contains(context.ConversationHistory,
            m => m.Role == ChatRole.System && m.Text?.Contains("[CURRENT_PLAN]") == true);
    }

    [Fact]
    public async Task AgentPlanMiddleware_InjectsDefaultInstructions_WhenEnabled()
    {
        var config = new PlanModeConfig { Enabled = true };
        var middleware = new AgentPlanAgentMiddleware(config);
        var context = CreateContext();

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Contains("[PLAN MODE ENABLED]", context.RunConfig.AdditionalSystemInstructions ?? "");
    }

    [Fact]
    public async Task AgentPlanMiddleware_InjectsCustomInstructions_WhenProvided()
    {
        var config = new PlanModeConfig
        {
            Enabled = true,
            CustomInstructions = "Custom: always make a plan first."
        };
        var middleware = new AgentPlanAgentMiddleware(config);
        var context = CreateContext();

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        Assert.Contains("Custom: always make a plan first.", context.RunConfig.AdditionalSystemInstructions ?? "");
        Assert.DoesNotContain("[PLAN MODE ENABLED]", context.RunConfig.AdditionalSystemInstructions ?? "");
    }

    [Fact]
    public async Task AgentPlanMiddleware_Disabled_NoInstructions()
    {
        var config = new PlanModeConfig { Enabled = false };
        var middleware = new AgentPlanAgentMiddleware(config);
        var context = CreateContext();

        await middleware.BeforeMessageTurnAsync(context, CancellationToken.None);

        // No instructions added when disabled
        Assert.True(string.IsNullOrEmpty(context.RunConfig.AdditionalSystemInstructions));
    }

    [Fact]
    public async Task AgentPlanMiddleware_InstructionsNotDuplicated_OnMultipleTurns()
    {
        var config = new PlanModeConfig { Enabled = true };
        var middleware = new AgentPlanAgentMiddleware(config);

        // First turn
        var ctx1 = CreateContext();
        await middleware.BeforeMessageTurnAsync(ctx1, CancellationToken.None);

        // Simulate the RunConfig being reused / instructions already present
        var ctx2 = CreateContext();
        ctx2.RunConfig.AdditionalSystemInstructions = ctx1.RunConfig.AdditionalSystemInstructions;
        await middleware.BeforeMessageTurnAsync(ctx2, CancellationToken.None);

        // [PLAN MODE ENABLED] marker should appear exactly once
        var count = CountOccurrences(ctx2.RunConfig.AdditionalSystemInstructions ?? "", "[PLAN MODE ENABLED]");
        Assert.Equal(1, count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P-16 through P-18: PlanModePersistentStateData edge cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetActivePlans_ReturnsOnlyNonCompleted()
    {
        var state = new PlanModePersistentStateData();

        var activePlan = PlanModePersistentStateData.CreatePlan("active", ["step"]);
        var completedPlan = PlanModePersistentStateData.CreatePlan("done", ["step"]).AsCompleted();

        state = state.WithPlan("conv-active", activePlan);
        state = state.WithPlan("conv-done", completedPlan);

        var active = state.GetActivePlans().ToList();

        Assert.Single(active);
        Assert.Equal("conv-active", active[0].ConversationId);
    }

    [Fact]
    public void WithCompletedPlansCleared_RemovesOnlyCompleted()
    {
        var state = new PlanModePersistentStateData();
        state = state.WithPlan("conv-a", PlanModePersistentStateData.CreatePlan("active", ["s"]));
        state = state.WithPlan("conv-b", PlanModePersistentStateData.CreatePlan("done", ["s"]).AsCompleted());

        var cleaned = state.WithCompletedPlansCleared();

        Assert.NotNull(cleaned.GetPlan("conv-a"));
        Assert.Null(cleaned.GetPlan("conv-b"));
    }

    [Fact]
    public void WithPlan_EmptyConversationId_Throws()
    {
        var state = new PlanModePersistentStateData();
        var plan = PlanModePersistentStateData.CreatePlan("goal", ["step"]);

        Assert.Throws<ArgumentException>(() => state.WithPlan("", plan));
    }

    // ═══════════════════════════════════════════════════════════════════
    // P-19 through P-20: GetDefaultPlanModeInstructions
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void DefaultInstructions_ContainsPlanModeEnabledMarker()
    {
        var instructions = AgentPlanAgentMiddleware.GetDefaultPlanModeInstructions();

        Assert.Contains("[PLAN MODE ENABLED]", instructions);
    }

    [Fact]
    public void DefaultInstructions_MentionsAllFiveFunctions()
    {
        var instructions = AgentPlanAgentMiddleware.GetDefaultPlanModeInstructions();

        Assert.Contains("create_plan", instructions);
        Assert.Contains("update_plan_step", instructions);
        Assert.Contains("add_plan_step", instructions);
        Assert.Contains("add_context_note", instructions);
        Assert.Contains("complete_plan", instructions);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static BeforeMessageTurnContext CreateContext()
    {
        var state = AgentLoopState.InitialSafe([], "test-run", ConvId, "TestAgent");
        var eventCoordinator = new HPD.Events.Core.EventCoordinator();
        var agentContext = new AgentContext("TestAgent", ConvId, state, eventCoordinator, null, null, CancellationToken.None);
        return agentContext.AsBeforeMessageTurn(
            new ChatMessage(ChatRole.User, "hello"),
            new List<ChatMessage>(),
            new AgentRunConfig());
    }

    private static BeforeMessageTurnContext CreateContextWithPlanState(PlanModePersistentStateData planState)
    {
        // Build an AgentLoopState that contains the plan in MiddlewareState
        var baseState = AgentLoopState.InitialSafe([], "test-run", ConvId, "TestAgent");
        var stateWithPlan = baseState with
        {
            MiddlewareState = baseState.MiddlewareState.WithPlanModePersistent(planState)
        };

        var eventCoordinator = new HPD.Events.Core.EventCoordinator();
        var agentContext = new AgentContext("TestAgent", ConvId, stateWithPlan, eventCoordinator, null, null, CancellationToken.None);
        return agentContext.AsBeforeMessageTurn(
            new ChatMessage(ChatRole.User, "hello"),
            new List<ChatMessage>(),
            new AgentRunConfig());
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
