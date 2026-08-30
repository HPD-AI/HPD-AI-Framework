using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModule("hpd.test.event-composition-aot-fixture-two", typeof(HPD.Agent.EventComposition.AotFixtureTwo.AotFixtureTwoJsonContext))]

namespace HPD.Agent.EventComposition.AotFixtureTwo;

[EventType("AOT_FIXTURE_TWO_EVENT", Durability = AgentEventDurability.Durable)]
public sealed record AotFixtureTwoEvent(string Value) : AgentEvent;

[JsonSerializable(typeof(AotFixtureTwoEvent))]
public sealed partial class AotFixtureTwoJsonContext : JsonSerializerContext;
