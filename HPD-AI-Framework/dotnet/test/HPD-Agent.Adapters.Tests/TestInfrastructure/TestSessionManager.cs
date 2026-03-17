using HPD.Agent;
using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.Adapters.Tests.TestInfrastructure;

/// <summary>
/// Minimal concrete subclass of SessionManager for unit tests.
/// </summary>
internal sealed class TestSessionManager : SessionManager
{
    public TestSessionManager(ISessionStore store) : base(store) { }
}
