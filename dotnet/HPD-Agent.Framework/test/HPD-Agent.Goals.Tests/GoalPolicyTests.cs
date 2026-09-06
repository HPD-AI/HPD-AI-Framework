using HPD.Agent.Goals;
using HPD.Agent.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests;

public class GoalPolicyTests
{
    private static GoalData NewGoal() => GoalTransitions.Create(new(), "Verify migration", new(), "g1", DateTimeOffset.UtcNow).Current!;
    private static GoalPolicyContext Context(GoalData goal, string execution = "e1") =>
        new(goal, execution, MessageTurnUsageSummary.Empty, false, true, true, false, 0, 3, null);

    [Fact]
    public async Task BlockerRequiresDistinctConsecutiveExecutionsAndResumeResetsAudit()
    {
        var goal = NewGoal();
        var report = new ReportGoalBlockerAction(GoalBlockerCategory.MissingArtifact, "Missing specification", "Supply specification");
        IGoalBlockerPolicy policy = DefaultGoalPolicies.Instance;
        for (var execution = 1; execution <= 3; execution++)
        {
            goal = GoalTransitions.ReportBlocker(goal, report, $"e{execution}", execution, DateTimeOffset.UtcNow);
            goal = GoalTransitions.ReportBlocker(goal, report, $"e{execution}", execution, DateTimeOffset.UtcNow);
            Assert.Equal(execution, goal.Blocker!.ConsecutiveExecutions);
            var result = await policy.EvaluateAsync(Context(goal, $"e{execution}"), default);
            Assert.Equal(execution == 3 ? GoalPolicyDisposition.Blocked : GoalPolicyDisposition.Continue, result.Disposition);
        }
        goal = GoalTransitions.ChangeStatus(goal, GoalStatus.Blocked, DateTimeOffset.UtcNow);
        goal = GoalTransitions.ChangeStatus(goal, GoalStatus.Active, DateTimeOffset.UtcNow);
        Assert.Null(goal.Blocker);
        goal = GoalTransitions.ReportBlocker(goal, report, "e4", 4, DateTimeOffset.UtcNow);
        Assert.Equal(1, goal.Blocker!.ConsecutiveExecutions);
    }

    [Fact]
    public void BlockerNormalizesWhitespaceButGapsAndDifferentConditionsResetCount()
    {
        var report = new ReportGoalBlockerAction(GoalBlockerCategory.Environment, "Disk full", "Free space");
        var goal = GoalTransitions.ReportBlocker(NewGoal(), report, "e1", 1, DateTimeOffset.UtcNow);
        goal = GoalTransitions.ReportBlocker(goal, report with { Description = " DISK   full " }, "e2", 2, DateTimeOffset.UtcNow);
        Assert.Equal(2, goal.Blocker!.ConsecutiveExecutions);
        goal = GoalTransitions.ReportBlocker(goal, report, "e4", 4, DateTimeOffset.UtcNow);
        Assert.Equal(1, goal.Blocker!.ConsecutiveExecutions);
        goal = GoalTransitions.ReportBlocker(goal, report with { RequiredChange = "Install SDK" }, "e5", 5, DateTimeOffset.UtcNow);
        Assert.Equal(1, goal.Blocker!.ConsecutiveExecutions);
        Assert.Throws<InvalidOperationException>(() => GoalTransitions.ReportBlocker(goal, report, "e3", 3, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task CompletionRequiresCurrentProposalEvidenceAndNoIncompletePlan()
    {
        IGoalCompletionPolicy policy = DefaultGoalPolicies.Instance;
        var goal = NewGoal();
        Assert.Equal(GoalPolicyDisposition.Rejected, (await policy.EvaluateAsync(Context(goal), default)).Disposition);
        goal = GoalTransitions.Propose(goal, new("Verified", [new("test", "Acceptance tests passed")], DateTimeOffset.UtcNow, "e1"), DateTimeOffset.UtcNow);
        Assert.Equal(GoalPolicyDisposition.Completed, (await policy.EvaluateAsync(Context(goal), default)).Disposition);
        Assert.Equal(GoalStatus.Active, goal.Status);
        var incomplete = goal with { CompletionProposal = goal.CompletionProposal! with { RemainingWork = ["Deployment verification pending"] } };
        Assert.Equal("required_work_remaining", (await policy.EvaluateAsync(Context(incomplete), default)).Reason);
        Assert.Equal(GoalPolicyDisposition.Rejected, (await policy.EvaluateAsync(Context(goal) with { HasIncompletePlan = true }, default)).Disposition);
        Assert.Equal(GoalPolicyDisposition.Rejected, (await policy.EvaluateAsync(Context(goal, "e2"), default)).Disposition);
    }

    [Fact]
    public async Task ContinuationDoesNotImplyStartupAndRequestSuspends()
    {
        IGoalContinuationPolicy policy = DefaultGoalPolicies.Instance;
        var context = Context(NewGoal());
        Assert.Equal("runtime_not_started", (await policy.EvaluateAsync(context with { RuntimeRunning = false }, default)).Reason);
        Assert.Equal(GoalPolicyDisposition.AwaitingInput, (await policy.EvaluateAsync(context with { HasUnresolvedRequest = true }, default)).Disposition);
        Assert.Equal(GoalPolicyDisposition.Paused, (await policy.EvaluateAsync(context with { HasProgress = false }, default)).Disposition);
        Assert.Equal(GoalPolicyDisposition.Continue, (await policy.EvaluateAsync(context, default)).Disposition);
    }

    [Fact]
    public void UsageSelectsModelTokensWithoutDoubleCountingTotalsOrOtherFamilies()
    {
        ProviderUsageMeasurement Measurement(string id, ProviderClientFamily family, UsageDetails? usage) =>
            new(id, "m1", 1, id, null, 1, ProviderOperationKind.ChatModelResponse, family,
                ProviderOperationOutcome.Succeeded, usage, "provider", "model", null);
        var measurements = new[]
        {
            Measurement("a", ProviderClientFamily.Chat, new() { InputTokenCount = 10, OutputTokenCount = 20, TotalTokenCount = 30 }),
            Measurement("b", ProviderClientFamily.Chat, new() { InputTokenCount = 3, OutputTokenCount = 4 }),
            Measurement("c", ProviderClientFamily.ImageGeneration, new() { TotalTokenCount = 999 }),
            Measurement("d", ProviderClientFamily.Realtime, null)
        };
        Assert.Equal(new GoalUsageProjection(37, GoalUsageQuality.Partial), DefaultGoalPolicies.Instance.Project(new(measurements)));
        Assert.Equal(new GoalUsageProjection(0, GoalUsageQuality.Unavailable), DefaultGoalPolicies.Instance.Project(MessageTurnUsageSummary.Empty));
        Assert.Throws<InvalidOperationException>(() => DefaultGoalPolicies.Instance.Project(new([measurements[0], measurements[0]])));
        Assert.Throws<InvalidOperationException>(() => DefaultGoalPolicies.Instance.Project(new([Measurement("x", ProviderClientFamily.Chat, new() { TotalTokenCount = -1 })])));
    }

    [Fact]
    public void PoliciesInheritIndependentlyAndUnknownKeysReportExactPaths()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IGoalAccountingPolicy>("custom", DefaultGoalPolicies.Instance);
        using var provider = services.BuildServiceProvider();
        var config = new GoalConfig { Policies = new() { Accounting = "custom" } };
        var resolver = new GoalPolicyResolver(config, provider);
        config.Policies.Completion = "mutated-after-build";
        Assert.Same(DefaultGoalPolicies.Instance, resolver.Resolve(new() { ToolAccess = GoalToolAccess.ReadOnly }).Accounting);
        var error = Assert.Throws<AgentRunConfigurationException>(() => resolver.Resolve(new() { Policies = new() { Blocker = "unknown" } }));
        Assert.Equal("Goals.Policies.Blocker", error.Path);
        Assert.Throws<AgentRunConfigurationException>(() => resolver.Resolve(new() { ToolAccess = (GoalToolAccess)999 }));
    }
}
