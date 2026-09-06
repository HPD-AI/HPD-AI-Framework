using System.Text.Json;
using HPD.Agent.Planning;
using HPD.Agent.Serialization;
using HPD.Events.Core;
using Xunit;

namespace HPD.Agent.Tests.Planning;

public class PlanJournalSnapshotTests
{
    private static Thread SeededThread(bool complete = false)
    {
        var thread = new Thread("session", "main", "agent");
        var plan = PlanModePersistentStateData.CreatePlan("Verify work", ["Inspect", "Test"]);
        if (complete) plan = plan.AsCompleted();
        thread.MiddlewareState[typeof(PlanModePersistentStateData).FullName!] = JsonSerializer.Serialize(
            new PlanModePersistentStateData().WithPlan("session", plan), SessionJsonContext.Combined.PlanModePersistentStateData);
        thread.MiddlewareState["other"] = "{}";
        return thread;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JournalReplacementCarriesPlanFactAndMiddlewareState(bool complete)
    {
        var thread = SeededThread(complete);
        var events = ThreadJournalEncoder.Encode(thread, []);
        var evt = Assert.Single(events.OfType<PlanUpdatedEvent>());
        Assert.Equal(PlanUpdateType.Snapshot, evt.UpdateType);
        Assert.Equal(complete, evt.Plan.IsComplete);
        Assert.Equal("session", evt.SessionId); Assert.Equal("main", evt.ThreadId);
        Assert.Equal("{}", Assert.Single(events.OfType<ThreadMiddlewareStateCommittedEvent>()).State["other"]);
        Assert.Equal(evt.EventId, Assert.Single(ThreadJournalEncoder.Encode(thread, []).OfType<PlanUpdatedEvent>()).EventId);
        var codec = CoreAgentEventComposition.Instance.Codec;
        var restored = Assert.IsType<PlanUpdatedEvent>(codec.DeserializeEvent(codec.Serialize(evt)));
        Assert.Equal(evt.Plan.Goal, restored.Plan.Goal);
        Assert.Equal(PlanUpdateType.Snapshot, restored.UpdateType);
    }

    [Fact]
    public async Task InitialThreadAndPublisherExposeDurablePlanFacts()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var thread = SeededThread();
        await store.SaveInitialThreadAsync(thread.SessionId, thread);
        using var coordinator = new EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var original = Assert.Single((await store.CollectThreadEventsAsync(new("session", "main")))!.OfType<PlanUpdatedEvent>());
        await publisher.PublishAsync(new("session", "main"), original with
        { EventId = Guid.NewGuid().ToString("N"), ThreadSequenceNumber = 0, UpdateType = PlanUpdateType.Completed, Plan = original.Plan.AsCompleted() });
        var latest = (await store.CollectThreadEventsAsync(new("session", "main")))!.OfType<PlanUpdatedEvent>().Last();
        Assert.True(latest.ThreadSequenceNumber > original.ThreadSequenceNumber);
        Assert.True(latest.Plan.IsComplete);
        var journal = (await store.CollectThreadEventsAsync(new("session", "main")))!;
        var recovered = ThreadProjector.Project("session", "main", journal, ThreadProjectionPurpose.ModelContext);
        var recoveredState = JsonSerializer.Deserialize(recovered.MiddlewareState[typeof(PlanModePersistentStateData).FullName!],
            SessionJsonContext.Combined.PlanModePersistentStateData)!;
        Assert.True(recoveredState.GetPlan("session")!.IsComplete);

    }

    [Fact]
    public async Task ForkSeedsPlanFactMatchingItsInheritedState()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var session = new Session("session");
        await store.SaveSessionAsync(session);
        var source = SeededThread(); source.Session = session;
        await store.SaveInitialThreadAsync(session.Id, source);
        var message = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "Continue") { MessageId = "m" };
        source.Messages.Add(message);
        await store.AppendThreadEventsAsync(new("session", "main"),
            ThreadMessageEventConverter.ToThreadEvents("session", "main", message), ThreadAppendCondition.Any);
        await using var agent = new Agent(new AgentConfig
        { Name = "agent", SessionStore = store, EventComposition = CoreAgentEventComposition.Instance }, baseClient: null, mergedOptions: null);
        var fork = await agent.ForkThreadAsync(source, "fork", "m", new ThreadForkOptions { OperationId = "plan-fork" });
        var journal = (await store.CollectThreadEventsAsync(new("session", "fork")))!;
        var snapshot = journal.OfType<PlanUpdatedEvent>().Last();
        Assert.Equal(PlanUpdateType.Snapshot, snapshot.UpdateType);
        Assert.Equal("fork", snapshot.ThreadId);
        var state = JsonSerializer.Deserialize(fork.MiddlewareState[typeof(PlanModePersistentStateData).FullName!],
            SessionJsonContext.Combined.PlanModePersistentStateData)!;
        Assert.Equal(state.GetPlan("session")!.Id, snapshot.Plan.Id);
    }

    [Fact]
    public void EmptyThreadDoesNotInventPlan()
        => Assert.Empty(ThreadJournalEncoder.Encode(new Thread("s", "t", "a"), []).OfType<PlanUpdatedEvent>());
}
