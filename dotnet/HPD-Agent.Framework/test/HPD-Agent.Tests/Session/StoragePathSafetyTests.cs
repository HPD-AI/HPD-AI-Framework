namespace HPD.Agent.Tests.Session;

public sealed class StoragePathSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"hpd-storage-path-safety-{Guid.NewGuid():N}");

    public StoragePathSafetyTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("nested/session")]
    [InlineData("nested\\session")]
    [InlineData("/absolute")]
    [InlineData("C:\\absolute")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    public async Task FileSessionStore_RejectsUnsafeSessionIdentifiers(string sessionId)
    {
        var store = new FileSessionStore(
            Path.Combine(_root, "sessions"),
            TestEventApplication.Codec);

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateSessionAsync(sessionId).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.DeleteSessionAsync(sessionId));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested\\thread")]
    [InlineData("C:\\absolute")]
    [InlineData("../thread")]
    [InlineData("nested/../thread")]
    [InlineData("nested//thread")]
    [InlineData("/thread")]
    public async Task FileSessionStore_RejectsUnsafeThreadIdentifiers(string threadId)
    {
        var store = new FileSessionStore(
            Path.Combine(_root, "sessions"),
            TestEventApplication.Codec);
        var session = await store.CreateSessionAsync("safe-session");

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateThreadAsync(
                session.Id,
                "agent",
                threadId).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.DeleteThreadAsync(session.Id, threadId));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/agent")]
    [InlineData("nested\\agent")]
    [InlineData("C:\\absolute")]
    public async Task JsonAgentStore_RejectsUnsafeAgentIdentifiers(string agentId)
    {
        var store = new JsonAgentStore(Path.Combine(_root, "agents"));
        var agent = new StoredAgent
        {
            Id = agentId,
            Name = "Unsafe",
            Config = new AgentConfig { Name = "Unsafe" }
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(agent));
        await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync(agentId));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(agentId));
    }

    [Fact]
    public async Task FileSessionStore_PreservesSafeHierarchicalThreadIdentifiers()
    {
        var store = new FileSessionStore(
            Path.Combine(_root, "sessions"),
            TestEventApplication.Codec);
        var session = await store.CreateSessionAsync("safe-session");

        var thread = await store.CreateThreadAsync(
            session.Id,
            "agent",
            "subagent/explore/invocation-1");

        Assert.Equal("subagent/explore/invocation-1", thread.Id);
        Assert.NotNull(await store.GetThreadAsync(new ThreadKey(session.Id, thread.Id)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
