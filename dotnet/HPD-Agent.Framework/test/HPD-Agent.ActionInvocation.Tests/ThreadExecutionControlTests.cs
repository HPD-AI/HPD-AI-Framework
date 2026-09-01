using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class ThreadExecutionControlTests
{
    [Fact]
    public async Task ControllerEnforcesExactExecutionForBusySteerCancelAndRelease()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var route = new ThreadKey("session", "child");
        await store.AppendThreadEventsAsync(
            route,
            [new ThreadCreatedEvent("child-agent", null, null, null, null, DateTime.UtcNow)
            {
                SessionId = route.SessionId,
                ThreadId = route.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        await using var agent = new Agent(
            new AgentConfig
            {
                Name = "child-agent",
                SessionStore = store,
                EventComposition = CoreAgentEventComposition.Instance
            },
            baseClient: null,
            mergedOptions: null);
        var controller = ThreadExecutionControllerRegistry.For(store);

        var first = await controller.TryAcquireAsync(new ThreadExecutionStartRequest(route, "execution-1", agent));
        Assert.True(first.Acquired);
        var busy = await controller.TryAcquireAsync(new ThreadExecutionStartRequest(route, "execution-2", agent));
        Assert.False(busy.Acquired);
        Assert.Equal("execution-1", busy.ActiveThreadExecutionId);

        var steerMismatch = await controller.SteerAsync(
            route,
            "execution-2",
            new UserMessagesInputEvent { Messages = [] });
        Assert.False(steerMismatch.Accepted);
        Assert.Equal(AgentInputDisposition.ActiveExecutionMismatch, steerMismatch.Disposition);
        var cancelMismatch = await controller.CancelAsync(route, "execution-2", "wrong execution");
        Assert.False(cancelMismatch.Accepted);
        Assert.Equal(AgentInputDisposition.ActiveExecutionMismatch, cancelMismatch.Disposition);

        await controller.ReleaseAsync(
            first.Lease!,
            new ThreadExecutionTerminalResult(ThreadExecutionOutcome.Succeeded));
        Assert.False((await controller.FindActiveAsync(route)).IsActive);
        var second = await controller.TryAcquireAsync(new ThreadExecutionStartRequest(route, "execution-2", agent));
        Assert.True(second.Acquired);
        await controller.ReleaseAsync(
            second.Lease!,
            new ThreadExecutionTerminalResult(ThreadExecutionOutcome.Cancelled));

        var events = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
                           route,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(1))))
            events.AddRange(batch.Events);
        Assert.Collection(
            events.OfType<ThreadExecutionStartedEvent>(),
            started => Assert.Equal("execution-1", started.ThreadExecutionId),
            started => Assert.Equal("execution-2", started.ThreadExecutionId));
        Assert.Collection(
            events.OfType<ThreadExecutionFinishedEvent>(),
            finished => Assert.Equal(ThreadExecutionOutcome.Succeeded, finished.Outcome),
            finished => Assert.Equal(ThreadExecutionOutcome.Cancelled, finished.Outcome));
    }
}
