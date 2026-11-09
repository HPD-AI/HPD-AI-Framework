using System.Text.Json;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Serialization;

public sealed class AgentEventPublisherLifecycleTests
{
    [Fact]
    public async Task CustomEvents_AreRoutedByDurability()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
        var composition = AgentEventComposition.Create(
        [
            new AgentEventModuleFragment
            {
                ModuleId = "tests.agent.events",
                Events =
                [
                    new AgentEventDescriptor
                    {
                        Discriminator = "DURABLE_PROBE_EVENT",
                        EventType = typeof(DurableProbeEvent),
                        JsonTypeInfo = options.GetTypeInfo(typeof(DurableProbeEvent)),
                        Durability = AgentEventDurability.Durable,
                        ModuleId = "tests.agent.events"
                    },
                    new AgentEventDescriptor
                    {
                        Discriminator = "LIVE_PROBE_EVENT",
                        EventType = typeof(LiveProbeEvent),
                        JsonTypeInfo = options.GetTypeInfo(typeof(LiveProbeEvent)),
                        Durability = AgentEventDurability.LiveOnly,
                        ModuleId = "tests.agent.events"
                    }
                ]
            }
        ]);
        var store = new InMemorySessionStore(composition.Codec);
        using var coordinator = new HPD.Events.Core.EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var thread = new ThreadKey("event-lifecycle-session", "main");

        var live = await publisher.PublishAsync(thread, new LiveProbeEvent("progress"));
        Assert.Equal(0, live.ThreadSequenceNumber);
        Assert.Null(await store.GetThreadEventHeadAsync(thread));

        var durable = await publisher.PublishAsync(thread, new DurableProbeEvent("committed"));
        Assert.True(durable.ThreadSequenceNumber > 0);
        var journal = await store.CollectThreadEventsAsync(thread.SessionId, thread.ThreadId);
        var persisted = Assert.Single(journal!);
        Assert.IsType<DurableProbeEvent>(persisted);
        Assert.DoesNotContain(journal!, value => value is LiveProbeEvent);
    }

    [Fact]
    public void CustomEvents_UseOneCatalogAndDigest()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
        var composition = AgentEventComposition.Create(
        [
            new AgentEventModuleFragment
            {
                ModuleId = "tests.agent.catalog",
                Events =
                [
                    Descriptor<DurableProbeEvent>("DURABLE_PROBE_EVENT", AgentEventDurability.Durable),
                    Descriptor<LiveProbeEvent>("LIVE_PROBE_EVENT", AgentEventDurability.LiveOnly)
                ]
            }
        ]);

        Assert.Equal(composition.Digest, composition.Codec.Digest);
        Assert.Collection(
            composition.Catalog.Events,
            durable =>
            {
                Assert.Equal("DURABLE_PROBE_EVENT", durable.Discriminator);
                Assert.Equal(AgentEventDurability.Durable, durable.Durability);
                Assert.Equal("tests.agent.catalog", durable.ModuleId);
            },
            live =>
            {
                Assert.Equal("LIVE_PROBE_EVENT", live.Discriminator);
                Assert.Equal(AgentEventDurability.LiveOnly, live.Durability);
                Assert.Equal("tests.agent.catalog", live.ModuleId);
            });
        Assert.Matches("^[0-9a-f]{64}$", composition.Digest);
        AgentEventDescriptor Descriptor<T>(string discriminator, AgentEventDurability durability)
            where T : AgentEvent => new()
            {
                Discriminator = discriminator,
                EventType = typeof(T),
                JsonTypeInfo = options.GetTypeInfo(typeof(T)),
                Durability = durability,
                ModuleId = "tests.agent.catalog"
            };
    }

    [Fact]
    public async Task Deltas_AreCoalescedAboveStoresWithoutDeltaStaging()
    {
        var inner = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var store = new NonDeltaSessionStore(inner);
        using var coordinator = new HPD.Events.Core.EventCoordinator();
        var publisher = new AgentEventPublisher(store, coordinator);
        var thread = new ThreadKey("delta-session", "main");

        await publisher.StageAndPublishDeltaAsync(thread, new TextDeltaEvent("Hello", "message-1"));
        await publisher.StageAndPublishDeltaAsync(thread, new TextDeltaEvent(" world", "message-1"));
        Assert.Null(await inner.GetThreadEventHeadAsync(thread));

        var result = await publisher.FinalizeAndPublishDeltasAsync(
            thread,
            new TextMessageEndEvent("message-1"));

        Assert.Equal(2, result.CommittedEvents.Count);
        var journal = await inner.CollectThreadEventsAsync(thread.SessionId, thread.ThreadId);
        Assert.Collection(
            journal!,
            delta =>
            {
                var text = Assert.IsType<TextDeltaEvent>(delta);
                Assert.Equal("Hello world", text.Text);
                Assert.True(text.ThreadSequenceNumber > 0);
            },
            end => Assert.IsType<TextMessageEndEvent>(end));
    }

    private sealed record DurableProbeEvent(string Value) : AgentEvent;

    private sealed record LiveProbeEvent(string Value) : AgentEvent;

    private sealed class NonDeltaSessionStore(ISessionStore inner) : ISessionStore
    {
        public AgentEventCodec EventCodec => inner.EventCodec;
        public StorePersistenceCapability PersistenceCapability => inner.PersistenceCapability;
        public Task<HPD.Agent.Session?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            inner.LoadSessionAsync(sessionId, cancellationToken);
        public Task SaveSessionAsync(HPD.Agent.Session session, CancellationToken cancellationToken = default) =>
            inner.SaveSessionAsync(session, cancellationToken);
        public ValueTask<bool> TryCreateSessionAsync(HPD.Agent.Session session, CancellationToken cancellationToken = default) =>
            inner.TryCreateSessionAsync(session, cancellationToken);
        public ValueTask<SessionPreparationResult> TryPrepareSessionAsync(
            HPD.Agent.Session session,
            CancellationToken cancellationToken = default) =>
            inner.TryPrepareSessionAsync(session, cancellationToken);
        public Task<List<string>> ListSessionIdsAsync(CancellationToken cancellationToken = default) =>
            inner.ListSessionIdsAsync(cancellationToken);
        public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            inner.DeleteSessionAsync(sessionId, cancellationToken);
        public ValueTask<ThreadEventAppendResult> AppendThreadEventsAsync(
            ThreadKey thread,
            IReadOnlyList<AgentEvent> events,
            ThreadAppendCondition condition = default,
            CancellationToken cancellationToken = default) =>
            inner.AppendThreadEventsAsync(thread, events, condition, cancellationToken);
        public ValueTask<ThreadJournalReplaceResult> ReplaceThreadEventsAsync(
            ThreadKey thread,
            IReadOnlyList<AgentEvent> events,
            ThreadJournalCursor expectedCursor,
            CancellationToken cancellationToken = default) =>
            inner.ReplaceThreadEventsAsync(thread, events, expectedCursor, cancellationToken);
        public ValueTask<ThreadDescriptor?> GetThreadAsync(
            ThreadKey thread,
            CancellationToken cancellationToken = default) =>
            inner.GetThreadAsync(thread, cancellationToken);
        public IAsyncEnumerable<ThreadDescriptor> ListThreadsAsync(
            string sessionId,
            ThreadListRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ListThreadsAsync(sessionId, request, cancellationToken);
        public ValueTask<ThreadEventHead?> GetThreadEventHeadAsync(
            ThreadKey thread,
            CancellationToken cancellationToken = default) =>
            inner.GetThreadEventHeadAsync(thread, cancellationToken);
        public IAsyncEnumerable<ThreadEventBatch> ReadThreadEventsAsync(
            ThreadKey thread,
            ThreadEventReadRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ReadThreadEventsAsync(thread, request, cancellationToken);
        public IAsyncEnumerable<ThreadEventBatch> ObserveThreadEventsAsync(
            ThreadKey thread,
            ThreadJournalCursor after,
            ThreadObservationOptions options,
            CancellationToken cancellationToken = default) =>
            inner.ObserveThreadEventsAsync(thread, after, options, cancellationToken);
        public Task DeleteThreadAsync(
            string sessionId,
            string threadId,
            CancellationToken cancellationToken = default) =>
            inner.DeleteThreadAsync(sessionId, threadId, cancellationToken);
        public Task<int> DeleteInactiveSessionsAsync(
            TimeSpan inactivityThreshold,
            bool dryRun = false,
            CancellationToken cancellationToken = default) =>
            inner.DeleteInactiveSessionsAsync(inactivityThreshold, dryRun, cancellationToken);
    }
}
