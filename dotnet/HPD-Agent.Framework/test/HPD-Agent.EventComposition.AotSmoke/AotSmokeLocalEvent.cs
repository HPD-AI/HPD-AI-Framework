using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModule("hpd.test.event-composition-aot-app", typeof(AotSmokeLocalJsonContext))]

[EventType("AOT_SMOKE_LOCAL_EVENT", Durability = AgentEventDurability.LiveOnly)]
internal sealed record AotSmokeLocalEvent(string Value) : AgentEvent;

[JsonSerializable(typeof(AotSmokeLocalEvent))]
internal sealed partial class AotSmokeLocalJsonContext : JsonSerializerContext;
