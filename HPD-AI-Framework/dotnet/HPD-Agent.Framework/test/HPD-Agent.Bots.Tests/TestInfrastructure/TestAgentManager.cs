using HPD.Agent;
using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.Bots.Tests.TestInfrastructure;

/// <summary>
/// Minimal concrete subclass of AgentManager for unit tests.
/// Used by adapter tests — no actual agent building required.
/// </summary>
internal sealed class TestAgentManager : AgentManager
{
    public TestAgentManager()
        : this(new WorkspaceAgentRepository(new InMemoryWorkspaceStore()))
    {
    }

    public TestAgentManager(IAgentRepository agentRepository) : base(agentRepository) { }

    protected override Task<Agent> BuildAgentAsync(string agentId, CancellationToken ct)
        => throw new NotSupportedException("BuildAgentAsync is not used in adapter tests.");

    protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(5);
}
