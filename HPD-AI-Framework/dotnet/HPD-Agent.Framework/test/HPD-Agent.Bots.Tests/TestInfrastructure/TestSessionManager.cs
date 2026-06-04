using HPD.Agent;
using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.Bots.Tests.TestInfrastructure;

/// <summary>
/// Minimal concrete subclass of SessionManager for unit tests.
/// </summary>
internal sealed class TestSessionManager : SessionManager
{
    public TestSessionManager()
        : this(new WorkspaceSessionRepository(new InMemoryWorkspaceStore()))
    {
    }

    public TestSessionManager(ISessionRepository repository) : base(repository) { }
}
