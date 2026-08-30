using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Serialization;

[assembly: HpdAgentEventModule("hpd.test.event-composition-aot-fixture", typeof(HPD.Agent.EventComposition.AotFixture.AotFixtureJsonContext))]

namespace HPD.Agent.EventComposition.AotFixture;

/// <summary>A durable event declared by the library referenced by the Native AOT smoke application.</summary>
[EventType("AOT_FIXTURE_EVENT", Durability = AgentEventDurability.Durable)]
public sealed record AotFixtureEvent(string Value) : AgentEvent;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(AotFixtureEvent))]
/// <summary>Source-generated JSON metadata for the Native AOT fixture event.</summary>
public partial class AotFixtureJsonContext : JsonSerializerContext;
