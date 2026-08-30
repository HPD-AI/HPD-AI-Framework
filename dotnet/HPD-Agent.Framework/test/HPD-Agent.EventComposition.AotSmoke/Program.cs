using System.Reflection;
using HPD.Agent;
using HPD.Agent.EventComposition.AotFixture;
using HPD.Agent.Serialization;

[assembly: HpdAgentApplication]

var applicationIdentity = Assembly.GetExecutingAssembly().GetName().Name!;
if (!AgentEventCompositionHost.TryGetApplication(applicationIdentity, out var composition))
    return 1;
if (!composition.Codec.TryGetByType(typeof(AotFixtureEvent), out var descriptor) ||
    descriptor.Durability != AgentEventDurability.Durable)
    return 2;

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
