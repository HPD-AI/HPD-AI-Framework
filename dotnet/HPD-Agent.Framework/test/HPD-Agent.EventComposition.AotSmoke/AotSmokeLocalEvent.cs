using HPD.Agent;
using HPD.Agent.Serialization;

[EventType("AOT_SMOKE_LOCAL_EVENT")]
internal sealed record AotSmokeLocalEvent(string Value) : AgentEvent;
