using FluentAssertions;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

public class AgentSessionServiceTests : IDisposable
{
    private readonly InMemorySessionStore _store = new();
    private readonly TestSessionManager _manager;
    private readonly AgentSessionService _service;

    public AgentSessionServiceTests()
    {
        _manager = new TestSessionManager(_store);
        _service = new AgentSessionService(_manager);
    }

    public void Dispose() => _manager.Dispose();

    [Fact]
    public async Task CreateSessionAsync_CreatesDefaultMainBranch()
    {
        var session = await _service.CreateSessionAsync();

        session.Id.Should().NotBeNullOrWhiteSpace();
        (await _store.LoadBranchAsync(session.Id, "main")).Should().NotBeNull();
    }

    [Fact]
    public async Task SearchSessionsAsync_FiltersByMetadata_AndOrdersByLastActivity()
    {
        var first = await _service.CreateSessionAsync(new CreateSessionRequest(
            "first",
            new Dictionary<string, object> { ["workspace"] = "a" }));
        await Task.Delay(20);
        var second = await _service.CreateSessionAsync(new CreateSessionRequest(
            "second",
            new Dictionary<string, object> { ["workspace"] = "a" }));
        await _service.CreateSessionAsync(new CreateSessionRequest(
            "third",
            new Dictionary<string, object> { ["workspace"] = "b" }));

        var sessions = await _service.SearchSessionsAsync(new SearchSessionsRequest(
            new Dictionary<string, object> { ["workspace"] = "a" },
            0,
            10));

        sessions.Select(s => s.Id).Should().Equal(second.Id, first.Id);
    }

    [Fact]
    public async Task UpdateSessionAsync_MergesMetadata_AndRemovesNullKeys()
    {
        var created = await _service.CreateSessionAsync(new CreateSessionRequest(
            "s1",
            new Dictionary<string, object>
            {
                ["keep"] = "yes",
                ["remove"] = "value"
            }));

        var updated = await _service.UpdateSessionAsync(
            created.Id,
            new UpdateSessionRequest(new Dictionary<string, object?>
            {
                ["new"] = "value",
                ["remove"] = null
            }));

        updated.Should().NotBeNull();
        updated!.Metadata.Should().ContainKey("keep");
        updated.Metadata.Should().ContainKey("new");
        updated.Metadata.Should().NotContainKey("remove");
        updated.LastActivity.Should().BeAfter(created.LastActivity);
    }

    [Fact]
    public async Task DeleteSessionAsync_DeletesStoreData_AndCleansLocks()
    {
        var created = await _service.CreateSessionAsync(new CreateSessionRequest("delete-me", null));
        _manager.TryAcquireStreamLock(created.Id, "main").Should().BeTrue();
        _manager.ReleaseStreamLock(created.Id, "main");

        (await _service.DeleteSessionAsync(created.Id)).Should().BeTrue();

        (await _store.LoadSessionAsync(created.Id)).Should().BeNull();
        _manager.TryAcquireStreamLock(created.Id, "main").Should().BeTrue();
    }

    private sealed class TestSessionManager : SessionManager
    {
        public TestSessionManager(ISessionStore store) : base(store) { }
    }
}
