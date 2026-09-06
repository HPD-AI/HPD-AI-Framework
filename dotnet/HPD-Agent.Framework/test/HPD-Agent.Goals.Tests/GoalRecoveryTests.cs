using HPD.Agent.Goals;
using HPD.Agent.Serialization;
using HPD.Events.Core;

namespace HPD.Agent.Tests;

public class GoalRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestoredPendingAttributionClosesOnceWithoutChargingDowntime(bool terminalCommitted)
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        using var coordinator = new EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var thread = new Thread("s", "t", "agent");
        var key = new ThreadKey("s", "t");
        await publisher.CommitAndPublishAsync(key, new ThreadMiddlewareStateCommittedEvent(new Dictionary<string, string>()));
        var initial = await GoalPersistence.ReadAsync(store, key, default);
        var started = DateTimeOffset.UtcNow.AddDays(-2);
        var state = GoalTransitions.Create(initial.Goal, "Verify restart", new(), "goal", started);
        state = GoalAccountingTransitions.Begin(state, "interrupted", "m", started);
        await GoalPersistence.CommitAsync(publisher, key, initial, state,
            new GoalStartedEvent(state.Current!, "created") { Timestamp = started, ThreadExecutionId = "interrupted" }, default);
        if (terminalCommitted)
            await publisher.CommitAndPublishAsync(key, new MessageTurnFinishedEvent("m", "c", "agent", "agent",
                TimeSpan.FromSeconds(2), MessageTurnUsageSummary.Empty) { Timestamp = started.AddSeconds(2), ThreadExecutionId = "interrupted" });
        var middleware = new GoalMiddleware(new(), null);
        var admitted = 0;
        ValueTask<bool> Admit(GoalContinuationInputEvent input) { admitted++; return ValueTask.FromResult(true); }
        await middleware.ReconcileAsync(store, publisher, thread, null, Admit);
        await middleware.ReconcileAsync(store, publisher, thread, null, Admit);
        var restored = (await GoalPersistence.ReadAsync(store, key, default)).Goal;
        Assert.Null(restored.PendingExecution);
        Assert.Equal(1, restored.Current!.Accounting.ExecutionCount);
        Assert.Equal(terminalCommitted ? TimeSpan.FromSeconds(2) : TimeSpan.Zero, restored.Current.Accounting.ExecutionTime);
        Assert.Equal(GoalUsageQuality.Unavailable, restored.Current.Accounting.UsageQuality);
        Assert.Equal(1, admitted);
    }

    [Fact]
    public async Task CommittedExternalQuotaClassificationSurvivesRecoveryWithoutBecomingBlocker()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        using var coordinator = new EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var key = new ThreadKey("s", "t");
        await publisher.CommitAndPublishAsync(key, new ThreadMiddlewareStateCommittedEvent(new Dictionary<string, string>()));
        var initial = await GoalPersistence.ReadAsync(store, key, default);
        var now = DateTimeOffset.UtcNow;
        var state = GoalAccountingTransitions.Begin(GoalTransitions.Create(initial.Goal, "Verify quota", new(), "goal", now), "e", "m", now);
        await GoalPersistence.CommitAsync(publisher, key, initial, state, new GoalStartedEvent(state.Current!, "created"), default);
        await publisher.CommitAndPublishAsync(key, new MessageTurnErrorEvent("m", "External quota exhausted", MessageTurnUsageSummary.Empty)
            { ThreadExecutionId = "e", Category = ErrorHandling.ErrorCategory.RateLimitTerminal });
        await new GoalMiddleware(new(), null).ReconcileAsync(store, publisher, new Thread("s", "t", "agent"), null,
            _ => throw new Exception("Quota-limited Goal must not resume"));
        var restored = (await GoalPersistence.ReadAsync(store, key, default)).Goal;
        Assert.Equal(GoalStatus.UsageLimited, restored.Current!.Status);
        Assert.Null(restored.Current.Blocker);
        Assert.Null(restored.PendingExecution);
    }

    [Fact]
    public async Task RepeatedRestoreAdmitsOnceAndNewRuntimeInvalidatesOldActivation()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        using var coordinator = new EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var thread = new Thread("s", "t", "agent");
        var key = new ThreadKey("s", "t");
        await publisher.CommitAndPublishAsync(key, new ThreadMiddlewareStateCommittedEvent(new Dictionary<string, string>()));
        var initial = await GoalPersistence.ReadAsync(store, key, default);
        var state = GoalTransitions.Create(initial.Goal, "Verify restart", new(), "goal", DateTimeOffset.UtcNow);
        await GoalPersistence.CommitAsync(publisher, key, initial, state, new GoalStartedEvent(state.Current!, "created"), default);
        var inputs = new List<GoalContinuationInputEvent>();
        ValueTask<bool> Admit(GoalContinuationInputEvent input) { inputs.Add(input); return ValueTask.FromResult(true); }
        var first = new GoalMiddleware(new(), null);
        await first.ReconcileAsync(store, publisher, thread, null, Admit);
        await first.ReconcileAsync(store, publisher, thread, null, Admit);
        Assert.Single(inputs);
        var second = new GoalMiddleware(new(), null);
        await second.ReconcileAsync(store, publisher, thread, null, Admit);
        Assert.Equal(2, inputs.Count);
        var restored = (await GoalPersistence.ReadAsync(store, key, default)).Goal;
        Assert.Same(restored, GoalTransitions.Consume(restored, inputs[0].GoalId, inputs[0].ExpectedRevision, inputs[0].Generation, DateTimeOffset.UtcNow));
        Assert.Null(GoalTransitions.Consume(restored, inputs[1].GoalId, inputs[1].ExpectedRevision, inputs[1].Generation, DateTimeOffset.UtcNow).Current!.Continuation);
    }

    [Fact]
    public async Task FailedAdmissionReleasesReservationAndRetainsActiveGoal()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        using var coordinator = new EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var thread = new Thread("s", "t", "agent");
        var key = new ThreadKey("s", "t");
        await publisher.CommitAndPublishAsync(key, new ThreadMiddlewareStateCommittedEvent(new Dictionary<string, string>()));
        var initial = await GoalPersistence.ReadAsync(store, key, default);
        var state = GoalTransitions.Create(initial.Goal, "Verify restart", new(), "goal", DateTimeOffset.UtcNow);
        await GoalPersistence.CommitAsync(publisher, key, initial, state, new GoalStartedEvent(state.Current!, "created"), default);
        var middleware = new GoalMiddleware(new(), null);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await middleware.ReconcileAsync(store, publisher, thread, null,
            _ => throw new InvalidOperationException("admission unavailable")));
        var restored = (await GoalPersistence.ReadAsync(store, key, default)).Goal.Current!;
        Assert.Equal(GoalStatus.Active, restored.Status);
        Assert.Null(restored.Continuation);
    }

    [Fact]
    public async Task RecoveryHonorsNoProgressPauseWithoutAdmittingWork()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        using var coordinator = new EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var thread = new Thread("s", "t", "agent");
        var key = new ThreadKey("s", "t");
        await publisher.CommitAndPublishAsync(key, new ThreadMiddlewareStateCommittedEvent(new Dictionary<string, string>()));
        var initial = await GoalPersistence.ReadAsync(store, key, default);
        var state = GoalTransitions.Create(initial.Goal, "Verify restart", new(), "goal", DateTimeOffset.UtcNow);
        state = state with { Current = state.Current! with { ConsecutiveNoProgressExecutions = 2 } };
        await GoalPersistence.CommitAsync(publisher, key, initial, state, new GoalStartedEvent(state.Current!, "created"), default);
        await new GoalMiddleware(new() { MaximumConsecutiveNoProgressExecutions = 2 }, null)
            .ReconcileAsync(store, publisher, thread, null, _ => throw new Exception("Must not admit"));
        Assert.Equal(GoalStatus.Paused, (await GoalPersistence.ReadAsync(store, key, default)).Goal.Current!.Status);
    }
}
