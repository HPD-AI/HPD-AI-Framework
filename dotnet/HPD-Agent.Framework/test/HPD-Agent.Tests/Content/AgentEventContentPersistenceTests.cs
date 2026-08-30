using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Content;

public class AgentEventContentPersistenceTests
{
    private static readonly AgentEventCodec Codec = AgentEventComposition.Create([
        new AgentEventModuleFragment
        {
            ModuleId = "hpd.agent.tests.content",
            Events = [new AgentEventDescriptor
            {
                Discriminator = "PERSISTABLE_CONTENT_TEST",
                EventType = typeof(PersistableContentTestEvent),
                JsonTypeInfo = AgentEventContentPersistenceTestJsonContext.Default.PersistableContentTestEvent,
                Durability = AgentEventDurability.Durable,
                ModuleId = "hpd.agent.tests.content"
            }]
        }
    ]).Codec;

    [Fact]
    public async Task PersistAsync_WhenEventRequestsContentPersistence_WritesSerializedEvent()
    {
        var store = new InMemoryContentStore();
        var evt = new PersistableContentTestEvent("hello")
        {
            EventId = "event-1",
            SessionId = "session-1",
            ThreadId = "thread-1",
            TraceId = "trace-1",
            SpanId = "span-1",
            Metadata = new AgentMetadata
            {
                AgentName = "TestAgent",
                AgentId = "agent-1"
            }
        };

        var info = await AgentEventContentPersistence.PersistAsync(
            store,
            Codec,
            evt,
            "default-scope");

        Assert.NotNull(info);
        Assert.Equal("event-1.json", info.Name);
        Assert.Equal("application/json", info.ContentType);
        Assert.Equal(ContentSource.Agent, info.Origin);
        Assert.Equal("memory-event", info.Tags?["kind"]);
        Assert.Equal("PERSISTABLE_CONTENT_TEST", info.Tags?["event.type"]);
        Assert.Equal("event-1", info.Tags?["event.id"]);
        Assert.Equal("session-1", info.Tags?["session"]);
        Assert.Equal("thread-1", info.Tags?["thread"]);
        Assert.Equal("trace-1", info.Tags?["trace"]);
        Assert.Equal("span-1", info.Tags?["span"]);
        Assert.Equal("TestAgent", info.Tags?["agent.name"]);
        Assert.Equal("agent-1", info.Tags?["agent.id"]);
        Assert.Equal("test", info.Tags?["test-tag"]);

        await using var opened = await store.OpenReadAsync(info.Address);
        Assert.NotNull(opened);
        using var reader = new StreamReader(opened!.Content);
        var json = await reader.ReadToEndAsync();

        Assert.Contains("\"type\":\"PERSISTABLE_CONTENT_TEST\"", json);
        Assert.Contains("\"value\":\"hello\"", json);
    }

    [Fact]
    public async Task PersistAsync_WhenEventDoesNotRequestContentPersistence_DoesNothing()
    {
        var store = new InMemoryContentStore();

        var info = await AgentEventContentPersistence.PersistAsync(
            store,
            HPD.Agent.Tests.TestEventApplication.Codec,
            new TextDeltaEvent("hello", "message-1"),
            "default-scope");

        Assert.Null(info);
        Assert.Empty(await store.QueryAsync(ContentScope.Create("default-scope")));
    }
}

internal sealed record PersistableContentTestEvent(string Value) : AgentEvent
{
    public override ContentPersistenceRequest? GetContentPersistenceRequest() => new()
    {
        Kind = "memory-event",
        Name = "event-1.json",
        Description = "Persisted test event",
        Origin = ContentSource.Agent,
        Tags = new Dictionary<string, string>
        {
            ["test-tag"] = "test"
        }
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PersistableContentTestEvent))]
internal partial class AgentEventContentPersistenceTestJsonContext : JsonSerializerContext;
