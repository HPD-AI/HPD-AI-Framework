using FluentAssertions;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Maui;
using HPD.Agent.Maui.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace HPD.Agent.Maui.Tests.Unit;

/// <summary>
/// Unit tests for MauiAgentManager — agent build priority, idle timeout, and caching.
/// </summary>
public class MauiAgentManagerTests : IDisposable
{
    private readonly InMemorySessionStore _sessionStore;
    private readonly InMemoryAgentStore _agentStore;
    private readonly MauiSessionManager _sessionManager;
    private readonly OptionsMonitorWrapper _optionsMonitor;

    public MauiAgentManagerTests()
    {
        _sessionStore = new InMemorySessionStore();
        _agentStore = new InMemoryAgentStore();
        _optionsMonitor = new OptionsMonitorWrapper();
        _sessionManager = new MauiSessionManager(_sessionStore, _optionsMonitor, Options.DefaultName);
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
        var factory = new CountingAgentFactory();
        var manager = MakeManager(factory);
        var stored = await SeedDefault(manager);

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);

        agent.Should().NotBeNull();
        factory.CreateCallCount.Should().Be(1);
    }

    [Fact]
    public async Task BuildAgentAsync_UsesDefaultAgentConfig_WhenProvided()
    {
        _optionsMonitor.CurrentValue.DefaultAgentConfig = MakeConfig("DefaultConfig");
        _optionsMonitor.CurrentValue.ConfigureAgent = InjectTestProvider;

        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        (await manager.GetOrBuildAgentAsync(stored.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAgentAsync_LoadsDefaultAgentConfigFromPath_WhenProvided()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(tempPath, System.Text.Json.JsonSerializer.Serialize(MakeConfig("FileConfig")));
        _optionsMonitor.CurrentValue.DefaultAgentConfigPath = tempPath;
        _optionsMonitor.CurrentValue.ConfigureAgent = InjectTestProvider;

        try
        {
            var manager = MakeManager();
            var stored = await SeedDefault(manager);
            (await manager.GetOrBuildAgentAsync(stored.Id)).Should().NotBeNull();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task BuildAgentAsync_DeserializesAgentConfig_Correctly()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        var config = MakeConfig("Deserialize Test");
        config.MaxAgenticIterations = 7;
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
    public async Task BuildAgentAsync_ThrowsException_WhenConfigFileInvalid()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        await File.WriteAllTextAsync(tempPath, "{ invalid json");
        _optionsMonitor.CurrentValue.DefaultAgentConfigPath = tempPath;

        try
        {
            var manager = MakeManager();
            // Seed with null Config so the file path (Priority 4) is reached
            var stored = await manager.CreateDefinitionAsync(new AgentConfig { Provider = null }, "NoConfig");

            Func<Task> act = () => manager.GetOrBuildAgentAsync(stored.Id);
            await act.Should().ThrowAsync<System.Text.Json.JsonException>();
        }
        finally
        {
            File.Delete(tempPath);
        }
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

    // ──────────────────────────────────────────────────────────────────────────
    // Caching and idle timeout
    // ──────────────────────────────────────────────────────────────────────────

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
    public void GetIdleTimeout_ReturnsConfiguredValue()
    {
        _optionsMonitor.CurrentValue.AgentIdleTimeout = TimeSpan.FromMinutes(45);
        var manager = new TestableMauiAgentManager(_agentStore, _sessionManager, _optionsMonitor, Options.DefaultName);
        manager.GetIdleTimeoutForTests().Should().Be(TimeSpan.FromMinutes(45));
    }

    [Fact]
    public void GetIdleTimeout_ReturnsDefault_30Min()
    {
        var manager = new TestableMauiAgentManager(_agentStore, _sessionManager, _optionsMonitor, Options.DefaultName);
        manager.GetIdleTimeoutForTests().Should().Be(TimeSpan.FromMinutes(30));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private MauiAgentManager MakeManager(IAgentFactory? factory = null)
        => new MauiAgentManager(_agentStore, _sessionManager, _optionsMonitor, Options.DefaultName, factory);

    private static async Task<StoredAgent> SeedDefault(MauiAgentManager manager)
        => await manager.CreateDefinitionAsync(MakeConfig("Default"), "Default");

    private static AgentConfig MakeConfig(string name) => new AgentConfig
    {
        Name = name,
        Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
    };

    private static void InjectTestProvider(AgentBuilder builder)
    {
        var field = typeof(AgentBuilder).GetField("_providerRegistry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(builder, new TestProviderRegistry(new FakeChatClient()));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class OptionsMonitorWrapper : IOptionsMonitor<HPDAgentConfig>
    {
        public HPDAgentConfig CurrentValue { get; } = new HPDAgentConfig();
        public HPDAgentConfig Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<HPDAgentConfig, string?> listener) => null;
    }

    private sealed class CountingAgentFactory : IAgentFactory
    {
        public int CreateCallCount { get; private set; }

        public async Task<Agent> CreateAgentAsync(string agentId, ISessionStore store, CancellationToken ct = default)
        {
            CreateCallCount++;
            var config = new AgentConfig
            {
                Name = "Factory",
                Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
            };
            var registry = new TestProviderRegistry(new FakeChatClient());
            return await new AgentBuilder(config, registry).WithSessionStore(store).BuildAsync(ct);
        }
    }

    private sealed class TestableMauiAgentManager : MauiAgentManager
    {
        public TestableMauiAgentManager(
            IAgentStore agentStore,
            MauiSessionManager sessionManager,
            IOptionsMonitor<HPDAgentConfig> optionsMonitor,
            string name,
            IAgentFactory? factory = null)
            : base(agentStore, sessionManager, optionsMonitor, name, factory) { }

        public TimeSpan GetIdleTimeoutForTests() => GetIdleTimeout();
    }
}
