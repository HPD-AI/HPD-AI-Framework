using FluentAssertions;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Unit tests for AspNetCoreAgentManager — agent build priority and idle timeout.
/// </summary>
public class AspNetCoreAgentManagerTests : IDisposable
{
    private readonly InMemorySessionStore _sessionStore;
    private readonly InMemoryAgentStore _agentStore;
    private readonly AspNetCoreSessionManager _sessionManager;
    private readonly OptionsMonitorWrapper _optionsMonitor;

    public AspNetCoreAgentManagerTests()
    {
        _sessionStore = new InMemorySessionStore();
        _agentStore = new InMemoryAgentStore();
        _optionsMonitor = new OptionsMonitorWrapper();
        _sessionManager = new AspNetCoreSessionManager(_sessionStore, _optionsMonitor, Options.DefaultName);
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Build priority
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAgentAsync_UsesIAgentFactory_WhenRegistered()
    {
        var factory = new CountingAgentFactory(_sessionStore);
        var manager = MakeManager(factory);
        var stored = await SeedDefault(manager);

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);

        agent.Should().NotBeNull();
        factory.CreateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task BuildAgentAsync_UsesDefaultAgentConfig_WhenProvided()
    {
        _optionsMonitor.CurrentValue.DefaultAgentConfig = MakeConfig("DefaultConfig Agent");
        _optionsMonitor.CurrentValue.ConfigureAgent = InjectTestProvider;

        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);
        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAgentAsync_UsesDefaultAgentConfigPath_WhenProvided()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        var config = MakeConfig("FileConfig Agent");
        await File.WriteAllTextAsync(tempPath, System.Text.Json.JsonSerializer.Serialize(config));

        _optionsMonitor.CurrentValue.DefaultAgentConfigPath = tempPath;
        _optionsMonitor.CurrentValue.ConfigureAgent = InjectTestProvider;

        try
        {
            var manager = MakeManager();
            var stored = await SeedDefault(manager);
            var agent = await manager.GetOrBuildAgentAsync(stored.Id);
            agent.Should().NotBeNull();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task BuildAgentAsync_FallsBackToEmptyBuilder_WhenNoConfig()
    {
        // No DefaultAgentConfig, no path, no factory — falls back to empty AgentBuilder
        _optionsMonitor.CurrentValue.ConfigureAgent = InjectTestProvider;

        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);
        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAgentAsync_CallsConfigureAgent_AfterConfig()
    {
        var called = false;
        _optionsMonitor.CurrentValue.DefaultAgentConfig = MakeConfig("X");
        _optionsMonitor.CurrentValue.ConfigureAgent = builder =>
        {
            called = true;
            InjectTestProvider(builder);
        };

        var manager = MakeManager();
        var stored = await SeedDefault(manager);
        await manager.GetOrBuildAgentAsync(stored.Id);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_CachesInstance_ByAgentId()
    {
        _optionsMonitor.CurrentValue.ConfigureAgent = InjectTestProvider;
        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        var a1 = await manager.GetOrBuildAgentAsync(stored.Id);
        var a2 = await manager.GetOrBuildAgentAsync(stored.Id);

        a1.Should().BeSameAs(a2);
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_Throws_WhenDefinitionMissing()
    {
        var manager = MakeManager();

        Func<Task> act = () => manager.GetOrBuildAgentAsync("no-such-agent");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Idle timeout
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetIdleTimeout_ReturnsConfiguredValue()
    {
        _optionsMonitor.CurrentValue.AgentIdleTimeout = TimeSpan.FromMinutes(60);
        var manager = MakeManager();
        manager.GetIdleTimeoutForTests().Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void GetIdleTimeout_ReturnsDefault_30Min()
    {
        var manager = MakeManager();
        manager.GetIdleTimeoutForTests().Should().Be(TimeSpan.FromMinutes(30));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private TestableAgentManager MakeManager(IAgentFactory? factory = null)
        => new TestableAgentManager(_agentStore, _sessionManager, _optionsMonitor, Options.DefaultName, factory);

    private static async Task<StoredAgent> SeedDefault(AgentManager manager)
    {
        return await manager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "Default",
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        }, "Default");
    }

    private static AgentConfig MakeConfig(string name) => new AgentConfig
    {
        Name = name,
        Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
    };

    private static void InjectTestProvider(AgentBuilder builder)
    {
        var chatClient = new FakeChatClient();
        var registry = new TestProviderRegistry(chatClient);
        var field = typeof(AgentBuilder).GetField("_providerRegistry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(builder, registry);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ──────────────────────────────────────────────────────────────────────────

    private class OptionsMonitorWrapper : IOptionsMonitor<HPDAgentConfig>
    {
        public HPDAgentConfig CurrentValue { get; } = new HPDAgentConfig();
        public HPDAgentConfig Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<HPDAgentConfig, string?> listener) => null;
    }

    private class CountingAgentFactory : IAgentFactory
    {
        private readonly ISessionStore _store;
        public int CreateCallCount { get; private set; }

        public CountingAgentFactory(ISessionStore store) => _store = store;

        public async Task<Agent> CreateAgentAsync(string agentId, ISessionStore store, CancellationToken ct = default)
        {
            CreateCallCount++;
            var config = MakeConfig("Factory");
            var chatClient = new FakeChatClient();
            var registry = new TestProviderRegistry(chatClient);
            return await new AgentBuilder(config, registry).WithSessionStore(store).BuildAsync(ct);
        }

        private static AgentConfig MakeConfig(string name) => new AgentConfig
        {
            Name = name,
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        };
    }

    /// <summary>Subclass that exposes the protected GetIdleTimeout for testing.</summary>
    private class TestableAgentManager : AspNetCoreAgentManager
    {
        public TestableAgentManager(
            IAgentStore agentStore,
            AspNetCoreSessionManager sessionManager,
            IOptionsMonitor<HPDAgentConfig> optionsMonitor,
            string name,
            IAgentFactory? agentFactory = null)
            : base(agentStore, sessionManager, optionsMonitor, name, agentFactory) { }

        public TimeSpan GetIdleTimeoutForTests() => GetIdleTimeout();
    }
}
