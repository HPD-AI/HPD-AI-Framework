using HPD.Agent.Goals;

namespace HPD.Agent.Tests;

public class GoalTerminalTests
{
    private static GoalPersistentState Pending(bool propose = false)
    {
        var now = DateTimeOffset.UtcNow;
        var state = GoalTransitions.Create(new(), "Verify outcome", new(), "g1", now);
        if (propose) state = state with { Current = GoalTransitions.Propose(state.Current!,
            new("Verified", [new("test", "Acceptance suite passed")], now, "e1"), now) };
        return GoalAccountingTransitions.Begin(state, "e1", "m1", now);
    }

    private static ValueTask<GoalTerminalResult> Close(GoalPersistentState state, GoalExecutionClosure closure, bool running)
        => GoalTerminalFinalizer.FinalizeAsync(state, "e1", "m1", new(1, 10), MessageTurnUsageSummary.Empty,
            TimeSpan.FromSeconds(1), closure,
            new(state.Current!, "e1", MessageTurnUsageSummary.Empty, false, true, running, false, 0, 3, null),
            new GoalPolicyResolver(new(), null).Resolve(null), DateTimeOffset.UtcNow, default);

    [Theory]
    [InlineData((int)GoalExecutionClosure.Cancelled, GoalStatus.Paused)]
    [InlineData((int)GoalExecutionClosure.Failed, GoalStatus.Faulted)]
    [InlineData((int)GoalExecutionClosure.RuntimeStopped, GoalStatus.Active)]
    [InlineData((int)GoalExecutionClosure.Interrupted, GoalStatus.Active)]
    [InlineData((int)GoalExecutionClosure.UsageLimited, GoalStatus.UsageLimited)]
    public async Task UnsuccessfulClosureSupersedesPendingCompletion(int closure, GoalStatus expected)
    {
        var result = await Close(Pending(true), (GoalExecutionClosure)closure, true);
        Assert.Equal(expected, result.State.Current!.Status);
        Assert.Null(result.State.Current.CompletionProposal);
        Assert.Null(result.State.Current.Continuation);
        Assert.Equal(1, result.State.Current.Accounting.ExecutionCount);
        Assert.DoesNotContain(result.Events, e => e is GoalCompletedEvent);
    }

    [Fact]
    public async Task SuccessfulClosureCompletesAfterAccountingAndDoesNotReserve()
    {
        var result = await Close(Pending(true), GoalExecutionClosure.Succeeded, true);
        Assert.IsType<GoalProgressAccountedEvent>(result.Events[0]);
        Assert.IsType<GoalCompletedEvent>(result.Events[1]);
        Assert.Equal(GoalStatus.Completed, result.State.Current!.Status);
        Assert.Null(result.State.Current.Continuation);
    }

    [Fact]
    public async Task DirectClosureLeavesActiveWithoutReservationOrImplicitRuntimeStartup()
    {
        var result = await Close(Pending(), GoalExecutionClosure.Succeeded, false);
        Assert.Equal(GoalStatus.Active, result.State.Current!.Status);
        Assert.Null(result.State.Current.Continuation);
        Assert.Equal("runtime_not_started", Assert.IsType<GoalContinuationSkippedEvent>(result.Events[^1]).Reason);
    }

    [Fact]
    public async Task RunningClosureReservesOnceAndReplayCannotReserveAgain()
    {
        var result = await Close(Pending(), GoalExecutionClosure.Succeeded, true);
        Assert.Equal(1, result.State.Current!.Continuation!.Generation);
        var duplicate = await Close(result.State, GoalExecutionClosure.Succeeded, true);
        Assert.Empty(duplicate.Events);
        Assert.Same(result.State, duplicate.State);
    }
}
