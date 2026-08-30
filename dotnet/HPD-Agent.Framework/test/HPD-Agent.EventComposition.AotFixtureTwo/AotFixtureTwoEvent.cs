using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.EventComposition.AotFixtureTwo;

[DurableEvent]
[EventType("AOT_FIXTURE_TWO_EVENT")]
public sealed record AotFixtureTwoEvent(string Value) : AgentEvent;
