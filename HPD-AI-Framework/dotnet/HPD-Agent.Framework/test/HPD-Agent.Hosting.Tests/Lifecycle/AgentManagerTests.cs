using FluentAssertions;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Hosting.Tests.Infrastructure;

namespace HPD.Agent.Hosting.Tests.Lifecycle;

/// <summary>
/// Tests for the AgentManager abstract base class.
/// Covers definition CRUD, instance caching (keyed by agentId), build locking, and idle eviction.
/// </summary>
public class AgentManagerTests : IDisposable
{
    private readonly InMemoryAgentStore _store;
    private readonly TestAgentManagerImpl _manager;

    public AgentManagerTests()
    {
        _store = new InMemoryAgentStore();
        _manager = new TestAgentManagerImpl(_store);
    }

    public void Dispose() => _manager.Dispose();

    // ──────────────────────────────────────────────────────────────────────────
    // Definition CRUD
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDefinitionAsync_PersistsToStore()
    {
        var config = MakeConfig("Agent A");
        var stored = await _manager.CreateDefinitionAsync(config, "Agent A");

        stored.Id.Should().NotBeNullOrWhiteSpace();
        stored.Name.Should().Be("Agent A");
        stored.Config.Should().BeSameAs(config);

        var loaded = await _store.LoadAsync(stored.Id);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Agent A");
    }

    [Fact]
    public async Task GetDefinitionAsync_ReturnsNull_WhenMissing()
    {
        var result = await _manager.GetDefinitionAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetDefinitionAsync_ReturnsDefinition_WhenExists()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");
        var loaded = await _manager.GetDefinitionAsync(stored.Id);
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(stored.Id);
    }

    [Fact]
    public async Task ListDefinitionsAsync_ReturnsAll()
    {
        await _manager.CreateDefinitionAsync(MakeConfig("A"), "A");
        await _manager.CreateDefinitionAsync(MakeConfig("B"), "B");
        await _manager.CreateDefinitionAsync(MakeConfig("C"), "C");

        var list = await _manager.ListDefinitionsAsync();
        list.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_PersistsNewConfig()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("Original"), "Original");
        var newConfig = MakeConfig("Updated");

        var updated = await _manager.UpdateDefinitionAsync(stored.Id, newConfig);

        updated.Config.Should().BeSameAs(newConfig);
        updated.UpdatedAt.Should().BeOnOrAfter(stored.UpdatedAt);

        var fromStore = await _store.LoadAsync(stored.Id);
        fromStore!.Config.Should().BeSameAs(newConfig);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_EvictsCachedInstance()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");
        await _manager.GetOrBuildAgentAsync(stored.Id);
        var buildCountAfterFirst = _manager.BuildCallCount;

        await _manager.UpdateDefinitionAsync(stored.Id, MakeConfig("X-updated"));

        // Next call must rebuild (cache was evicted)
        await _manager.GetOrBuildAgentAsync(stored.Id);
        _manager.BuildCallCount.Should().Be(buildCountAfterFirst + 1);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_Throws_WhenAgentNotFound()
    {
        Func<Task> act = () => _manager.UpdateDefinitionAsync("missing", MakeConfig("X"));
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*missing*");
    }

    [Fact]
    public async Task DeleteDefinitionAsync_RemovesFromStore_AndEvictsCache()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("D"), "D");
        await _manager.GetOrBuildAgentAsync(stored.Id);

        await _manager.DeleteDefinitionAsync(stored.Id);

        var fromStore = await _manager.GetDefinitionAsync(stored.Id);
        fromStore.Should().BeNull();

        var cached = _manager.GetAgent(stored.Id);
        cached.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Instance caching
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrBuildAgentAsync_BuildsOnFirstCall()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");

        await _manager.GetOrBuildAgentAsync(stored.Id);

        _manager.BuildCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_ReturnsSameInstance_ForSameAgentId()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");

        var a1 = await _manager.GetOrBuildAgentAsync(stored.Id);
        var a2 = await _manager.GetOrBuildAgentAsync(stored.Id);

        a1.Should().BeSameAs(a2);
        _manager.BuildCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_CreatesDifferentInstances_ForDifferentAgentIds()
    {
        var s1 = await _manager.CreateDefinitionAsync(MakeConfig("A"), "A");
        var s2 = await _manager.CreateDefinitionAsync(MakeConfig("B"), "B");

        var a1 = await _manager.GetOrBuildAgentAsync(s1.Id);
        var a2 = await _manager.GetOrBuildAgentAsync(s2.Id);

        a1.Should().NotBeSameAs(a2);
        _manager.BuildCallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_PreventsConcurrentBuilds_WithPerAgentLock()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");

        var buildStarted = new TaskCompletionSource();
        var buildCanComplete = new TaskCompletionSource();

        _manager.OnBuildStarted = () => buildStarted.SetResult();
        _manager.OnBuildWait = () => buildCanComplete.Task;

        var task1 = Task.Run(() => _manager.GetOrBuildAgentAsync(stored.Id));
        await buildStarted.Task;

        var task2 = Task.Run(() => _manager.GetOrBuildAgentAsync(stored.Id));
        await Task.Delay(80);

        buildCanComplete.SetResult();

        var a1 = await task1;
        var a2 = await task2;

        a1.Should().BeSameAs(a2);
        _manager.BuildCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_UpdatesLastAccessed_OnEveryAccess()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");
        await _manager.GetOrBuildAgentAsync(stored.Id);
        var first = _manager.GetLastAccessedTime(stored.Id);

        await Task.Delay(15);
        await _manager.GetOrBuildAgentAsync(stored.Id);
        var second = _manager.GetLastAccessedTime(stored.Id);

        second.Should().BeAfter(first);
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_Throws_WhenDefinitionNotFound()
    {
        Func<Task> act = () => _manager.GetOrBuildAgentAsync("no-such-agent");
        await act.Should().ThrowAsync<KeyNotFoundException>().WithMessage("*no-such-agent*");
    }

    [Fact]
    public async Task GetAgent_ReturnsNull_WhenNotBuilt()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");
        _manager.GetAgent(stored.Id).Should().BeNull();
    }

    [Fact]
    public async Task GetAgent_ReturnsInstance_AfterBuild()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");
        var built = await _manager.GetOrBuildAgentAsync(stored.Id);

        _manager.GetAgent(stored.Id).Should().BeSameAs(built);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Idle eviction — purely time-based, no IsStreaming guard
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvictIdleAgents_RemovesAgents_OlderThanTimeout()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");
        await _manager.GetOrBuildAgentAsync(stored.Id);

        _manager.SetLastAccessedTime(stored.Id, DateTime.UtcNow.AddMinutes(-35));
        _manager.TriggerEviction();
        await Task.Delay(50);

        _manager.GetAgent(stored.Id).Should().BeNull();
    }

    [Fact]
    public async Task EvictIdleAgents_KeepsAgents_NewerThanTimeout()
    {
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");
        var first = await _manager.GetOrBuildAgentAsync(stored.Id);

        _manager.SetLastAccessedTime(stored.Id, DateTime.UtcNow.AddMinutes(-10));
        _manager.TriggerEviction();
        await Task.Delay(50);

        // Should still be cached — no rebuild needed
        var second = await _manager.GetOrBuildAgentAsync(stored.Id);
        second.Should().BeSameAs(first);
        _manager.BuildCallCount.Should().Be(1);
    }

    [Fact]
    public async Task EvictIdleAgents_EvictsOldAgents_EvenIfStreamLockHeld()
    {
        // KEY BEHAVIORAL CHANGE: IsStreaming is gone — time-based eviction only.
        // The stream lock (held externally by SessionManager) does NOT protect the
        // agent cache from eviction. After eviction the next stream request rebuilds.
        var stored = await _manager.CreateDefinitionAsync(MakeConfig("X"), "X");
        await _manager.GetOrBuildAgentAsync(stored.Id);

        _manager.SetLastAccessedTime(stored.Id, DateTime.UtcNow.AddMinutes(-35));
        _manager.TriggerEviction();
        await Task.Delay(50);

        // Evicted — GetAgent returns null, even though a stream might be running
        _manager.GetAgent(stored.Id).Should().BeNull();
    }

    [Fact]
    public void GetIdleTimeout_UsedForEvictionCalculation()
    {
        _manager.GetIdleTimeoutForTests().Should().Be(TimeSpan.FromMinutes(30));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static AgentConfig MakeConfig(string name) => new AgentConfig
    {
        Name = name,
        MaxAgenticIterations = 5,
        Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Test double
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class TestAgentManagerImpl : AgentManager
    {
        public int BuildCallCount { get; private set; }
        public Action? OnBuildStarted { get; set; }
        public Func<Task>? OnBuildWait { get; set; }

        public TestAgentManagerImpl(IAgentStore store) : base(store) { }

        protected override async Task<Agent> BuildAgentAsync(StoredAgent stored, CancellationToken ct)
        {
            OnBuildStarted?.Invoke();
            if (OnBuildWait != null)
                await OnBuildWait();

            BuildCallCount++;

            var chatClient = new FakeChatClient();
            var registry = new TestProviderRegistry(chatClient);
            return await new AgentBuilder(stored.Config, registry)
                .WithSessionStore(new InMemorySessionStore())
                .BuildAsync(ct);
        }

        protected override TimeSpan GetIdleTimeout() => TimeSpan.FromMinutes(30);

        public TimeSpan GetIdleTimeoutForTests() => GetIdleTimeout();

        public void TriggerEviction()
        {
            var method = typeof(AgentManager).GetMethod(
                "EvictIdleAgents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method!.Invoke(this, [null]);
        }

        public DateTime GetLastAccessedTime(string agentId)
        {
            var field = typeof(AgentManager).GetField("_agents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var agents = field!.GetValue(this)!;
            var tryGet = agents.GetType().GetMethod("TryGetValue")!;
            var args = new object[] { agentId, null! };
            var found = (bool)tryGet.Invoke(agents, args)!;
            if (!found) return DateTime.MinValue;
            var entry = args[1];
            return (DateTime)entry!.GetType().GetProperty("LastAccessed")!.GetValue(entry)!;
        }

        public void SetLastAccessedTime(string agentId, DateTime time)
        {
            var field = typeof(AgentManager).GetField("_agents",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var agents = field!.GetValue(this)!;
            var tryGet = agents.GetType().GetMethod("TryGetValue")!;
            var args = new object[] { agentId, null! };
            var found = (bool)tryGet.Invoke(agents, args)!;
            if (!found) return;
            var entry = args[1];
            entry!.GetType().GetProperty("LastAccessed")!.SetValue(entry, time);
        }
    }
}
