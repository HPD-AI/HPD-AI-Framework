using HPD.Agent.Goals;
using HPD.Agent.Serialization;
using HPD.Events.Core;

namespace HPD.Agent.Tests;

public class GoalPersistenceTests
{
    [Fact]
    public async Task GoalAndLifecycleCommitTogetherAndStaleWriterCannotOverwrite()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        using var coordinator = new EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var key = new ThreadKey("s1", "t1");
        await publisher.CommitAndPublishAsync(key, new ThreadMiddlewareStateCommittedEvent(
            new Dictionary<string, string> { ["another.middleware"] = "preserved" }));
        var first = await GoalPersistence.ReadAsync(store, key, default);
        var stale = await GoalPersistence.ReadAsync(store, key, default);
        var created = GoalTransitions.Create(first.Goal, "Verify migration", new(), "g1", DateTimeOffset.UtcNow);
        var result = await GoalPersistence.CommitAsync(publisher, key, first, created,
            new GoalStartedEvent(created.Current!, "created"), default);
        Assert.Equal(2, result.CommittedEvents.Count);
        Assert.Equal(result.CommittedEvents[0].ThreadSequenceNumber + 1, result.CommittedEvents[1].ThreadSequenceNumber);
        var reloaded = await GoalPersistence.ReadAsync(store, key, default);
        Assert.Equal("g1", reloaded.Goal.Current!.GoalId);
        Assert.Equal("preserved", reloaded.MiddlewareState["another.middleware"]);
        await Assert.ThrowsAsync<ThreadAppendConflictException>(async () =>
            await GoalPersistence.CommitAsync(publisher, key, stale, created,
                new GoalStartedEvent(created.Current!, "stale"), default));
        Assert.Equal(reloaded.Cursor, (await GoalPersistence.ReadAsync(store, key, default)).Cursor);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{invalid")]
    [InlineData("{\"current\":{\"goalId\":\"g1\",\"objective\":\"x\",\"status\":\"future\",\"revision\":1}}")]
    public void CorruptPersistedStateFailsClosed(string json)
    {
        Assert.ThrowsAny<Exception>(() => GoalPersistence.Read(new Dictionary<string, string>
        {
            [GoalPersistence.StateKey] = json
        }));
    }
}
