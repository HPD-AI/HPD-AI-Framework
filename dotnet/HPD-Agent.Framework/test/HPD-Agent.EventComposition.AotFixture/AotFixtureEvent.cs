using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.EventComposition.AotFixture;

/// <summary>A durable event declared by the library referenced by the Native AOT smoke application.</summary>
[DurableEvent]
[PersistEventContent("aot-fixture")]
[EventType("AOT_FIXTURE_EVENT")]
public sealed record AotFixtureEvent(string Value) : AgentEvent;
