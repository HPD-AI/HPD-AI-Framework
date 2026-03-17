using HPD.Agent;
using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.Adapters.Tests.TestInfrastructure;

/// <summary>
/// Minimal concrete subclass of AgentManager for unit tests.
/// Used by adapter tests — no actual agent building required.
/// </summary>
internal sealed class TestAgentManager : AgentManager
{
    public TestAgentManager(IAgentStore agentStore) : base(agentStore) { }

    protected override Task<Agent> BuildAgentAsync(StoredAgent stored, CancellationToken ct)
        => throw new NotSupportedException("BuildAgentAsync is not used in adapter tests.");

    protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(5);
}
