using HPD.Agent;

namespace HPD.Agent.EventComposition.AotFixture;

public sealed record DeclaredFixtureEvent(string Value) : AgentEvent;
