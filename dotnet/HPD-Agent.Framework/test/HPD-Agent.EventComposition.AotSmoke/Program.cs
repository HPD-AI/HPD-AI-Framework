using System.Reflection;
using HPD.Agent;
using HPD.Agent.EventComposition.AotFixture;
using HPD.Agent.EventComposition.AotFixtureTwo;
using HPD.Agent.Serialization;

var applicationIdentity = Assembly.GetExecutingAssembly().GetName().Name!;
if (!AgentEventCompositionHost.TryGetApplication(applicationIdentity, out var composition))
    return 1;
if (!composition.Codec.TryGetByType(typeof(AotFixtureEvent), out var descriptor) ||
    descriptor.Durability != AgentEventDurability.Durable ||
    descriptor.ContentPolicy?.Kind != "aot-fixture")
    return 2;
if (!composition.Codec.TryGetByType(typeof(AotFixtureTwoEvent), out _) ||
    !composition.Codec.TryGetByType(typeof(AotSmokeLocalEvent), out _))
    return 4;

var content = new InMemoryContentStore();
using var coordinator = new HPD.Events.Core.EventCoordinator();
var publisher = new AgentEventPublisher(
    new InMemorySessionStore(composition.Codec),
    coordinator,
    new AgentEventContentArchiver(content));
var retained = new AotFixtureEvent("archived")
{
    EventId = "retained-event",
    SessionId = "aot-session",
    ThreadId = "main"
};
await publisher.PublishLiveAsync(retained);
await publisher.PublishLiveAsync(retained);
if ((await content.QueryAsync(ContentScope.Create("aot-session"))).Count != 1)
    return 5;

_ = new AgentBuilder().WithInMemorySessionStore();
_ = new AgentBuilder().WithFileSessionStore(Path.Combine(Path.GetTempPath(), "hpd-builder-dx"));

var root = args.Length == 1
    ? Path.GetFullPath(args[0])
    : Path.Combine(Path.GetTempPath(), $"hpd-event-aot-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);

var firstStore = new FileSessionStore(root, composition.Codec);
await firstStore.AppendThreadEventAsync(
    "aot-session",
    "main",
    new AotFixtureEvent("persisted"));

var reopenedStore = new FileSessionStore(root, composition.Codec);
var events = await reopenedStore.CollectThreadEventsAsync("aot-session", "main");
return events?.OfType<AotFixtureEvent>().SingleOrDefault()?.Value == "persisted" ? 0 : 3;
