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
                ModuleId = "hpd.agent.tests.content",
                ContentPolicy = new AgentEventContentPolicy(
                    "memory-event",
                    "application/json",
                    ContentSource.Agent)
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

        var publisher = new AgentEventPublisher(
            new InMemorySessionStore(Codec),
            new HPD.Events.Core.EventCoordinator(),
            new AgentEventContentArchiver(store));
        await publisher.PublishLiveAsync(evt);
        await publisher.PublishLiveAsync(evt);
        var items = await store.QueryAsync(ContentScope.Create("session-1"));
        var info = Assert.Single(items);

        Assert.NotNull(info);
        Assert.Equal("events/PERSISTABLE_CONTENT_TEST/event-1.json", info.Name);
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

        await new AgentEventContentArchiver(store).ArchiveAsync(
            HPD.Agent.Tests.TestEventApplication.Codec,
            new TextDeltaEvent("hello", "message-1") { SessionId = "default-scope" });

        Assert.Empty(await store.QueryAsync(ContentScope.Create("default-scope")));
    }

    [Fact]
    public async Task ArchiveFailure_IsObservableAndDoesNotEscapePublication()
    {
        AgentEventArchiveDiagnostic? observed = null;
        var archiver = new AgentEventContentArchiver(
            new ThrowingContentStore(),
            diagnostic => observed = diagnostic);

        await archiver.ArchiveAsync(Codec, new PersistableContentTestEvent("hello")
        {
            EventId = "event-failure",
            SessionId = "session-1"
        });

        Assert.True(observed.HasValue);
        Assert.Equal(typeof(PersistableContentTestEvent), observed.Value.EventType);
        Assert.Equal("Content archival failed.", observed.Value.Reason);
        Assert.IsType<InvalidOperationException>(observed.Value.Exception);
        Assert.Equal(HPD.Events.EventKind.Diagnostic, observed.Value.Kind);
    }

    private sealed class ThrowingContentStore : IContentStore
    {
        public ValueTask<ContentInfo> WriteAsync(ContentScope scope, Stream data, ContentMetadata metadata,
            ContentWriteOptions options, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("write failed");

        public ValueTask<ContentReadResult?> OpenReadAsync(ContentAddress address, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ContentReadResult?>(null);
        public ValueTask<Uri?> CreateReadUriAsync(ContentAddress address, TimeSpan expiresIn, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Uri?>(null);
        public ValueTask<ContentInfo?> StatAsync(ContentAddress address, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ContentInfo?>(null);
        public ValueTask DeleteAsync(ContentAddress address, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<ContentInfo>> QueryAsync(ContentScope scope, ContentQuery? query = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ContentInfo>>([]);
    }
}

internal sealed record PersistableContentTestEvent(string Value) : AgentEvent;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PersistableContentTestEvent))]
internal partial class AgentEventContentPersistenceTestJsonContext : JsonSerializerContext;
