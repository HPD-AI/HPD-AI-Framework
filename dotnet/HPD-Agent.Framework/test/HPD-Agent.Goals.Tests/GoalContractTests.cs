using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Goals;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using HPD.Agent.Validation;
using HPD.Serialization;

namespace HPD.Agent.Tests.Goals;

public sealed class GoalContractTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-05T12:00:00Z");
    private static GoalPersistentState Create() => GoalTransitions.Create(new(), "Finish and verify", new(), "goal-a", Now);

    [Theory]
    [InlineData(GoalStatus.Active)]
    [InlineData(GoalStatus.Paused)]
    [InlineData(GoalStatus.AwaitingInput)]
    [InlineData(GoalStatus.Blocked)]
    [InlineData(GoalStatus.UsageLimited)]
    public void AnUnfinishedGoalCannotBeReplaced(GoalStatus status)
    {
        var state = Create();
        state = state with { Current = state.Current! with { Status = status } };
        Assert.Throws<InvalidOperationException>(() => GoalTransitions.Create(state, "Other work", new(), "goal-b", Now));
        Assert.Equal("goal-a", state.Current.GoalId);
    }

    [Theory]
    [InlineData(GoalStatus.Completed)]
    [InlineData(GoalStatus.Faulted)]
    public void TerminalIdentityCannotResumeButAllowsNewGoal(GoalStatus status)
    {
        var state = Create();
        var terminal = GoalTransitions.ChangeStatus(state.Current!, status, Now);
        Assert.Throws<InvalidOperationException>(() => GoalTransitions.ChangeStatus(terminal, GoalStatus.Active, Now));
        var replacement = GoalTransitions.Create(state with { Current = terminal }, "New work", new(), "goal-b", Now);
        Assert.Equal("goal-b", replacement.Current!.GoalId);
    }

    [Fact]
    public void DuplicateOrEditedContinuationIsANoop()
    {
        var state = Create();
        var reserved = GoalTransitions.Reserve(state.Current!, "execution-a", Now);
        state = state with { Current = reserved };
        var consumed = GoalTransitions.Consume(state, "goal-a", reserved.Revision, reserved.ContinuationGeneration, Now);
        Assert.Null(consumed.Current!.Continuation);
        Assert.Equal(reserved.Revision + 1, consumed.Current.Revision);
        Assert.Same(consumed, GoalTransitions.Consume(consumed, "goal-a", reserved.Revision, reserved.ContinuationGeneration, Now));
        var edited = state with { Current = GoalTransitions.Edit(reserved, "New complete objective", 4000, Now) };
        Assert.Same(edited, GoalTransitions.Consume(edited, "goal-a", reserved.Revision, reserved.ContinuationGeneration, Now));
    }

    [Fact]
    public void PendingCompletionCannotScheduleMoreWork()
    {
        var proposal = new GoalCompletionProposal("Verified", [new("test", "Checks passed")], Now, "execution-a");
        var pending = GoalTransitions.Propose(Create().Current!, proposal, Now);
        Assert.Equal(GoalStatus.Active, pending.Status);
        Assert.Throws<InvalidOperationException>(() => GoalTransitions.Reserve(pending, "execution-a", Now));
        var paused = GoalTransitions.ChangeStatus(pending, GoalStatus.Paused, Now);
        Assert.Null(paused.CompletionProposal);
    }

    [Fact]
    public void ParallelMutationMustRevalidateRevision()
    {
        var initial = Create();
        var edited = initial with { Current = GoalTransitions.Edit(initial.Current!, "Revised requirement", 4000, Now) };
        Assert.Throws<InvalidOperationException>(() => GoalTransitions.Require(edited, initial.Current!.GoalId, initial.Current.Revision));
    }

    [Fact]
    public void ForkResetsExecutionOwnershipAndUsage()
    {
        var source = GoalTransitions.Reserve(Create().Current!, "execution-a", Now) with
        {
            Accounting = new() { TokensUsed = 90, ExecutionCount = 2 },
            Blocker = new(GoalBlockerCategory.Authority, "x", "No authority", "Approval", [], 2, Now, Now, "execution-a", 2)
        };
        var fork = GoalTransitions.ForkPaused(source, "goal-b", Now);
        Assert.Equal(GoalStatus.Paused, fork.Status);
        Assert.Equal("goal-b", fork.GoalId);
        Assert.Equal(source.Objective, fork.Objective);
        Assert.Null(fork.Continuation);
        Assert.Null(fork.Blocker);
        Assert.Equal(0, fork.Accounting.TokensUsed);
        Assert.Equal(0, fork.ContinuationGeneration);
        Assert.NotNull(source.Continuation);
    }

    [Fact]
    public void UnknownOrCorruptStateCannotContinue()
    {
        var state = Create();
        var corrupt = state with { Current = state.Current! with { Status = (GoalStatus)99 } };
        Assert.Throws<InvalidOperationException>(() => GoalTransitions.Consume(corrupt, "goal-a", 1, 1, Now));
        Assert.Throws<InvalidOperationException>(() => GoalTransitions.Create(corrupt, "New", new(), "b", Now));
    }

    [Fact]
    public void ConfigHelperMutatesOnlyTheAuthoritativeObject()
    {
        var builder = new AgentBuilder();
        builder.WithGoals(config => config.MaximumObjectiveLength = 512);
        var config = builder.Config.Goals;
        builder.WithGoals(value => value.AllowModelCreatedGoals = false);
        Assert.Same(config, builder.Config.Goals);
        Assert.Equal(512, config!.MaximumObjectiveLength);
        Assert.False(config.AllowModelCreatedGoals);
        Assert.DoesNotContain(builder.Middlewares, middleware => middleware.GetType().Name.Contains("Goal"));
    }

    [Theory]
    [InlineData(HpdConfigFormat.Json)]
    [InlineData(HpdConfigFormat.Yaml)]
    public void ConfigRoundTripsPreserveIndependentInheritance(HpdConfigFormat format)
    {
        var config = new AgentConfig { Goals = new() { Policies = new() { Completion = "verified" } } };
        var restored = HpdAgentConfigSerializer.Deserialize(HpdAgentConfigSerializer.Serialize(config, format), format)!;
        Assert.Equal("verified", restored.Goals!.Policies.Completion);
        var run = new AgentRunConfig { Goals = new() { ToolAccess = GoalToolAccess.ReadOnly, Policies = new() { Blocker = "strict" } } };
        var composition = ProviderComposition.Create([]);
        var restoredRun = HpdAgentConfigSerializer.DeserializeRunConfig(HpdAgentConfigSerializer.Serialize(run, composition, format), composition, format)!;
        Assert.Equal(GoalToolAccess.ReadOnly, restoredRun.Goals!.ToolAccess);
        Assert.Null(restoredRun.Goals.Policies!.Completion);
        Assert.Equal("strict", restoredRun.Goals.Policies.Blocker);
    }

    [Fact]
    public void RunCaptureCannotBeChangedByTheSubmitter()
    {
        var source = new AgentRunConfig { Goals = new() { ToolAccess = GoalToolAccess.ReadOnly, Policies = new() { Completion = "verified" } } };
        var snapshot = AgentRunConfigSnapshot.Capture(source, null)!;
        source.Goals.ToolAccess = GoalToolAccess.All;
        source.Goals.Policies!.Completion = "other";
        Assert.Equal(GoalToolAccess.ReadOnly, snapshot.Goals!.ToolAccess);
        Assert.Equal("verified", snapshot.Goals.Policies!.Completion);
        Assert.Contains(nameof(AgentRunConfig.Goals), AgentRunConfigSnapshot.CapturedPropertyNames);
    }

    [Fact]
    public void GeneratedStateMetadataPreservesReservationAndPendingEvidence()
    {
        var reserved = Create() with { Current = GoalTransitions.Reserve(Create().Current!, "execution-a", Now) };
        var json = JsonSerializer.Serialize(reserved, GoalJsonContext.Default.GoalPersistentState);
        var restored = JsonSerializer.Deserialize(json, GoalJsonContext.Default.GoalPersistentState)!;
        Assert.Equal(reserved.Current, restored.Current);
        GoalTransitions.Validate(restored.Current!);
        Assert.Contains("\"status\":\"active\"", json);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            json.Replace("\"active\"", "\"future-status\""), GoalJsonContext.Default.GoalPersistentState));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            json.Replace("\"active\"", "99"), GoalJsonContext.Default.GoalPersistentState));
    }

    [Fact]
    public void GeneratedActionsRoundTripAndRejectUnknownDiscriminators()
    {
        GoalAction[] actions =
        [
            new CreateGoalAction("Finish"), new GetGoalAction(),
            new ProposeGoalCompletionAction("Verified", [new("test", "Passed")]),
            new ReportGoalBlockerAction(GoalBlockerCategory.Authority, "Unavailable", "Approval"),
            new PauseGoalAction(), new ResumeGoalAction(), new EditGoalAction("Revised"), new ClearGoalAction()
        ];
        foreach (var action in actions)
        {
            var json = JsonSerializer.Serialize(action, GoalJsonContext.Default.GoalAction);
            var restored = JsonSerializer.Deserialize(json, GoalJsonContext.Default.GoalAction);
            Assert.IsType(action.GetType(), restored);
            Assert.Equal(json, JsonSerializer.Serialize(restored, GoalJsonContext.Default.GoalAction));
        }
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            "{\"action\":\"replaceEverything\"}", GoalJsonContext.Default.GoalAction));
        var configJson = JsonSerializer.Serialize(new GoalRunConfig { ToolAccess = GoalToolAccess.ReadOnly }, GoalJsonContext.Default.GoalRunConfig);
        Assert.Contains("\"toolAccess\":\"readOnly\"", configJson);
    }

    [Fact]
    public void MinimalDeserializedConfigurationRetainsProviderCollectionDefaults()
    {
        var config = HpdAgentConfigSerializer.Deserialize("{\"goals\":{\"enabled\":true}}")!;
        Assert.NotNull(config.ProviderDefaults);
        Assert.Empty(config.ProviderDefaults);
        Assert.NotNull(config.ProviderProfiles);
        Assert.Empty(config.ProviderProfiles);
        Assert.NotNull(config.Goals!.Policies);
    }

    [Fact]
    public void InvalidDefaultsHavePropertyPathDiagnostics()
    {
        var errors = AgentConfigValidator.Validate(new AgentConfig
        {
            Goals = new() { MaximumObjectiveLength = 0, RequiredConsecutiveBlockerExecutions = 0, Policies = new() { Completion = "" } }
        });
        Assert.Contains(errors, error => error.Contains("Goals.MaximumObjectiveLength"));
        Assert.Contains(errors, error => error.Contains("Goals.RequiredConsecutiveBlockerExecutions"));
        Assert.Contains(errors, error => error.Contains("Goals.Policies.Completion"));
    }
}
