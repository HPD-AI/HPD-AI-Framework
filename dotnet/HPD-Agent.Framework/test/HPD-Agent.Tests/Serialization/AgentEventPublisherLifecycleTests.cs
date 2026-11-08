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

    private sealed record DurableProbeEvent(string Value) : AgentEvent;

    private sealed record LiveProbeEvent(string Value) : AgentEvent;
}
