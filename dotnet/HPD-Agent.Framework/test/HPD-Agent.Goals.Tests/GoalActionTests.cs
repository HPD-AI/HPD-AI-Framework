using HPD.Agent.Goals;

namespace HPD.Agent.Tests;

public class GoalActionTests
{
    [Fact]
    public void GeneratedGoalIsOneDescribedFunctionWithEightActionsAndRestrictedComposition()
    {
        var factory = HPD.Agent.Generated.ToolHarnessRegistry.All.Single(x => x.ToolHarnessType == typeof(AgentGoalToolHarness));
        var function = Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(Assert.Single(factory.CreateFunctions(new AgentGoalToolHarness(), null, null)));
        Assert.Equal("goal", function.Name);
        Assert.True(GoalMiddleware.IsGoalFunction(function));
        Assert.False(string.IsNullOrWhiteSpace(function.Description));
        Assert.Equal(8, function.OperationContract!.Actions.Count);
        var blockerSchema = function.JsonSchema.GetProperty("properties").GetProperty("operation").GetProperty("oneOf")
            .EnumerateArray().Single(branch => branch.GetProperty("properties").GetProperty("action").GetProperty("const").GetString() == "reportBlocker");
        Assert.Contains("Environment", blockerSchema.GetProperty("properties").GetProperty("category").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("Environment: The environment makes every in-scope approach impossible.",
            blockerSchema.GetProperty("properties").GetProperty("category").GetProperty("description").GetString());
        var read = GoalActionComposition.Restrict(function, GoalToolAccess.ReadOnly, true);
        Assert.Equal("get", Assert.Single(read.OperationContract.Actions).Key);
        Assert.Single(read.JsonSchema.GetProperty("properties").GetProperty("operation").GetProperty("oneOf").EnumerateArray());
        var noCreate = GoalActionComposition.Restrict(function, GoalToolAccess.All, false);
        Assert.Equal(7, noCreate.OperationContract.Actions.Count);
        Assert.DoesNotContain("create", noCreate.OperationContract.Actions.Keys);
        using var forged = System.Text.Json.JsonDocument.Parse("{\"operation\":{\"action\":\"clear\"}}");
        Assert.Throws<InvalidOperationException>(() => read.FinalArgumentBinder!(forged.RootElement));
    }

    [Fact]
    public void ParallelMutationUsesObservedRevisionAndCannotOverwriteNewerObjective()
    {
        var config = new GoalConfig();
        var now = DateTimeOffset.UtcNow;
        var state = GoalTransitions.Create(new(), "Original", config, "g1", now);
        var edit = new GoalToolMutation(new EditGoalAction("Revised"), "g1", 1);
        var pause = new GoalToolMutation(new PauseGoalAction(), "g1", 1);
        var edited = GoalActionTransition.Apply(state, edit, config, "e1", now);
        Assert.Equal("Revised", edited.State.Current!.Objective);
        Assert.Throws<InvalidOperationException>(() => GoalActionTransition.Apply(edited.State, pause, config, "e1", now));
    }

    [Fact]
    public void ClearDoesNotReportCompletionAndAllowsNewIdentity()
    {
        var config = new GoalConfig();
        var now = DateTimeOffset.UtcNow;
        var state = GoalTransitions.Create(new(), "Original", config, "g1", now);
        var cleared = GoalActionTransition.Apply(state, new(new ClearGoalAction(), "g1", 1), config, "e1", now);
        Assert.Null(cleared.State.Current);
        Assert.IsType<GoalClearedEvent>(cleared.Event);
        var created = GoalActionTransition.Apply(cleared.State, new(new CreateGoalAction("New outcome"), null, null), config, "e1", now);
        Assert.NotEqual("g1", created.State.Current!.GoalId);
    }

    [Fact]
    public void ModelCreationDisabledDoesNotDisableDeterministicTransition()
    {
        var config = new GoalConfig { AllowModelCreatedGoals = false };
        Assert.Throws<InvalidOperationException>(() => GoalActionTransition.Apply(new(),
            new(new CreateGoalAction("Outcome"), null, null), config, "e1", DateTimeOffset.UtcNow));
        Assert.NotNull(GoalTransitions.Create(new(), "Outcome", config, "g1", DateTimeOffset.UtcNow).Current);
    }

    [Fact]
    public void RestrictedAccessRejectsForgedMutationAndHiddenRead()
    {
        AgentGoalToolHarness.CheckAccess(new GetGoalAction(), GoalToolAccess.ReadOnly);
        Assert.Throws<InvalidOperationException>(() => AgentGoalToolHarness.CheckAccess(new ClearGoalAction(), GoalToolAccess.ReadOnly));
        Assert.Throws<InvalidOperationException>(() => AgentGoalToolHarness.CheckAccess(new GetGoalAction(), GoalToolAccess.Hidden));
    }
}
