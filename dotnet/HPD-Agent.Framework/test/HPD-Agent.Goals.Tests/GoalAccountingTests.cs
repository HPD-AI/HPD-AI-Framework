using HPD.Agent.Goals;

namespace HPD.Agent.Tests;

public class GoalAccountingTests
{
    [Fact]
    public void ClearAndReplacementRetainOldTurnUsageWithoutChargingNewGoal()
    {
        var now = DateTimeOffset.UtcNow;
        var state = GoalTransitions.Create(new(), "First", new(), "g1", now);
        state = GoalAccountingTransitions.Begin(state, "e1", "m1", now);
        state = GoalActionTransition.Apply(state, new(new ClearGoalAction(), "g1", 1), new(), "e1", now).State;
        state = GoalTransitions.Create(state, "Second", new(), "g2", now);
        var result = GoalAccountingTransitions.Close(state, "e1", "m1", new(1, 10), new(30, GoalUsageQuality.Exact), TimeSpan.FromSeconds(2), now);
        Assert.True(result.Applied);
        Assert.Equal("g1", result.AccountedGoal!.GoalId);
        Assert.Equal(30, result.AccountedGoal.Accounting.TokensUsed);
        Assert.Equal("g2", result.State.Current!.GoalId);
        Assert.Equal(0, result.State.Current.Accounting.TokensUsed);
        Assert.Null(result.State.PendingExecution);
    }

    [Fact]
    public void ReplayDeduplicatesByJournalAndCountsMessageTurnsSeparatelyFromExecutions()
    {
        var now = DateTimeOffset.UtcNow;
        var state = GoalAccountingTransitions.Begin(GoalTransitions.Create(new(), "Outcome", new(), "g1", now), "e1", "m1", now);
        var first = GoalAccountingTransitions.Close(state, "e1", "m1", new(1, 10), new(10, GoalUsageQuality.Exact), TimeSpan.FromSeconds(1), now);
        var duplicate = GoalAccountingTransitions.Close(first.State, "e1", "m1", new(1, 10), new(10, GoalUsageQuality.Exact), TimeSpan.FromSeconds(1), now);
        Assert.False(duplicate.Applied);
        var next = GoalAccountingTransitions.Begin(first.State, "e1", "m2", now);
        var second = GoalAccountingTransitions.Close(next, "e1", "m2", new(1, 20), new(5, GoalUsageQuality.Partial), TimeSpan.FromSeconds(2), now);
        Assert.Equal(1, second.State.Current!.Accounting.ExecutionCount);
        Assert.Equal(15, second.State.Current.Accounting.TokensUsed);
        Assert.Equal(TimeSpan.FromSeconds(3), second.State.Current.Accounting.ExecutionTime);
        Assert.Equal(GoalUsageQuality.Partial, second.State.Current.Accounting.UsageQuality);
        Assert.False(GoalAccountingTransitions.Close(second.State, "e1", "m1", new(1, 10), new(10, GoalUsageQuality.Exact), TimeSpan.Zero, now).Applied);
    }

    [Fact]
    public void UnsettledAttributionMustBeRecoveredBeforeAnotherTurnBegins()
    {
        var now = DateTimeOffset.UtcNow;
        var state = GoalAccountingTransitions.Begin(GoalTransitions.Create(new(), "Outcome", new(), "g1", now), "e1", "m1", now);
        Assert.Same(state, GoalAccountingTransitions.Begin(state, "e1", "m1", now));
        Assert.Throws<InvalidOperationException>(() => GoalAccountingTransitions.Begin(state, "e2", "m2", now));
    }
}
