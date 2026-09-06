using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Infrastructure;

/// <summary>Shared event composition for tests that exercise the core framework surface.</summary>
internal static class TestEventComposition
{
    internal static AgentEventComposition Current { get; } =
        AgentEventComposition.Create([CoreAgentEventModule.Fragment]);
}

// Test-only migration alias for legacy tests while they move to TestEventComposition.Current.
namespace HPD.Agent.Serialization;

internal static class CoreAgentEventComposition
{
    internal static AgentEventComposition Instance =>
        HPD.Agent.Tests.Infrastructure.TestEventComposition.Current;
}
