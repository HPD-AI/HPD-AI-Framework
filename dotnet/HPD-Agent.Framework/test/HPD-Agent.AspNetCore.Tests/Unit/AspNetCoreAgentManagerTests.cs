using FluentAssertions;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.ToolHarness.Coding;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ServiceProvider _serviceProvider;

    public AspNetCoreAgentManagerTests()
    {
        _sessionStore = new InMemorySessionStore();
        _agentStore = new InMemoryAgentStore();
        _optionsMonitor = new OptionsMonitorWrapper();
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
        _sessionManager = new AspNetCoreSessionManager(_sessionStore, _optionsMonitor, Options.DefaultName);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
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
    public async Task BuildAgentAsync_UsesDefaultAgent_WhenProvided()
    {
        _optionsMonitor.CurrentValue.DefaultAgent = MakeConfig("Default Agent");
        AddContributor(InjectTestProvider);

        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);
        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAgentAsync_UsesDefaultAgentPath_WhenProvided()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        var config = MakeConfig("FileConfig Agent");
        await File.WriteAllTextAsync(tempPath, System.Text.Json.JsonSerializer.Serialize(config));

        _optionsMonitor.CurrentValue.DefaultAgentPath = tempPath;
        AddContributor(InjectTestProvider);

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
        // No DefaultAgent, no path, no factory — falls back to empty AgentBuilder
        AddContributor(InjectTestProvider);

        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);
        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAgentAsync_AppliesAgentContributors_AfterConfig()
    {
        var called = false;
        var owner = new HpdContributionOwner("hpd.test.package", "test");
        HpdAgentContributionContext? capturedContext = null;
        _optionsMonitor.CurrentValue.DefaultAgent = MakeConfig("X");
        _optionsMonitor.CurrentValue.AgentContributors.Add(
            "test.agent",
            new CapturingAgentBuilderContributor((builder, context) =>
            {
                called = true;
                capturedContext = context;
                InjectTestProvider(builder);
            }),
            owner);

        var manager = MakeManager();
        var stored = await SeedDefault(manager);
        await manager.GetOrBuildAgentAsync(stored.Id);

        called.Should().BeTrue();
        capturedContext.Should().NotBeNull();
        capturedContext!.Owner.Should().Be(owner);
        capturedContext.AgentId.Should().Be(stored.Id);
        capturedContext.Services.Should().BeSameAs(_serviceProvider);
    }

    [Fact]
    public async Task BuildAgentAsync_AppliesProviderContributions_BeforeBuild()
    {
        _optionsMonitor.CurrentValue.DefaultAgent = MakeConfig("Provider Contribution");
        _optionsMonitor.CurrentValue.ProviderContributions.AddProviderFactory(
            "test",
            _ => new TestChatClientProvider(new FakeChatClient()),
            new HpdContributionOwner("hpd.test.provider", "test"));

        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);

        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_CachesInstance_ByAgentId()
    {
        AddContributor(InjectTestProvider);
        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        var a1 = await manager.GetOrBuildAgentAsync(stored.Id);
        var a2 = await manager.GetOrBuildAgentAsync(stored.Id);

        a1.Should().BeSameAs(a2);
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_Builds_WhenDefinitionMissing()
    {
        AddContributor(InjectTestProvider);
        var manager = MakeManager();

        var agent = await manager.GetOrBuildAgentAsync("no-such-agent");

        agent.Should().NotBeNull();
        agent.AgentId.Should().Be("no-such-agent");
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_DoesNotPersistMissingDefinition_WhenPersistAgentDefinitionsOnBuildFalse()
    {
        AddContributor(InjectTestProvider);
        _optionsMonitor.CurrentValue.PersistAgentDefinitionsOnBuild = false;
        var manager = MakeManager();

        var agent = await manager.GetOrBuildAgentAsync("runtime-only");

        agent.Should().NotBeNull();
        agent.AgentId.Should().Be("runtime-only");
        (await _agentStore.LoadAsync("runtime-only")).Should().BeNull();
    }

    [Fact]
    public async Task BuildAgentAsync_UsesStoredToolHarnessConfig()
    {
        AddContributor(builder =>
        {
            builder.WithToolHarnessCatalogFrom<CodingToolHarness>();
            InjectTestProvider(builder);
        });
        var manager = MakeManager();
        var stored = await manager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "Coding",
            Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" } },
            ToolHarnesses = [nameof(CodingToolHarness)]
        }, "Coding");

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);

        var toolNames = agent.DefaultOptions?.Tools?
            .OfType<AIFunction>()
            .Select(tool => tool.Name)
            .ToArray();

        toolNames.Should().NotBeNull();
        toolNames.Should().Contain([
            "ReadFile",
            "ListDirectory",
            "GlobSearch",
            "Grep",
            "ExecuteCommand"
        ]);
    }

    [Fact]
    public async Task BuildAgentAsync_UsesStoredToolHarnessConfig_WithRuntimeProvider()
    {
        AddContributor(builder =>
            builder.WithToolHarnessCatalogFrom<CodingToolHarness>());
        var manager = MakeManager();
        var stored = await manager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "Coding",
            ToolHarnesses = [nameof(CodingToolHarness)]
        }, "Coding");

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);

        var toolNames = agent.DefaultOptions?.Tools?
            .OfType<AIFunction>()
            .Select(tool => tool.Name)
            .ToArray();

        toolNames.Should().NotBeNull();
        toolNames.Should().Contain([
            "ReadFile",
            "ListDirectory",
            "GlobSearch",
            "Grep",
            "ExecuteCommand"
        ]);
    }

    [Fact]
    public async Task BuildAgentAsync_RuntimeProvider_UsesExplicitSummarizerProvider()
    {
        AddContributor(InjectTestProviderRegistry);
        var manager = MakeManager();
        var stored = await manager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "Runtime Summarizing",
            Compaction = new CompactionConfig
            {
                Enabled = true,
                Strategy = new SummarizingCompactionOptions
                {
                    SummarizerProvider = new ClientProviderConfig {
                        ProviderKey = "test",
                        ModelName = "summarizer-model"
                    }
                }
            }
        }, "Runtime Summarizing");

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);

        agent.Should().NotBeNull();
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
        => new TestableAgentManager(_agentStore, _sessionManager, _optionsMonitor, _serviceProvider, Options.DefaultName, factory);

    private void AddContributor(Action<AgentBuilder> configure)
        => _optionsMonitor.CurrentValue.AgentContributors.Add(new DelegateAgentBuilderContributor(configure));

    private static async Task<StoredAgent> SeedDefault(AgentManager manager)
    {
        return await manager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "Default",
            Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" } }
        }, "Default");
    }

    private static AgentConfig MakeConfig(string name) => new AgentConfig
    {
        Name = name,
        Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" } }
    };

    private static void InjectTestProvider(AgentBuilder builder)
    {
        builder.Config.SetChatClientConfig(new ClientProviderConfig
        {
            ProviderKey = "test",
            ModelName = "test-model"
        });

        var chatClient = new FakeChatClient();
        var registry = new TestProviderRegistry(chatClient);
        var field = typeof(AgentBuilder).GetField("_providerRegistry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(builder, registry);
    }

    private static void InjectTestProviderRegistry(AgentBuilder builder)
    {
        var chatClient = new FakeChatClient();
        var registry = new TestProviderRegistry(chatClient);
        var field = typeof(AgentBuilder).GetField("_providerRegistry",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(builder, registry);
    }

    private sealed class CapturingAgentBuilderContributor : IAgentBuilderContributor
    {
        private readonly Action<AgentBuilder, HpdAgentContributionContext> _configure;

        public CapturingAgentBuilderContributor(Action<AgentBuilder, HpdAgentContributionContext> configure)
        {
            _configure = configure;
        }

        public void ConfigureAgent(AgentBuilder builder, HpdAgentContributionContext context)
            => _configure(builder, context);
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
            Clients = new AgentClientConfig { Chat = new ClientProviderConfig { ProviderKey = "test", ModelName = "test-model" } }
        };
    }

    /// <summary>Subclass that exposes the protected GetIdleTimeout for testing.</summary>
    private class TestableAgentManager : AspNetCoreAgentManager
    {
        public TestableAgentManager(
            IAgentStore agentStore,
            AspNetCoreSessionManager sessionManager,
            IOptionsMonitor<HPDAgentConfig> optionsMonitor,
            IServiceProvider serviceProvider,
            string name,
            IAgentFactory? agentFactory = null)
            : base(agentStore, sessionManager, optionsMonitor, serviceProvider, name, new InMemoryContentStore(), agentFactory) { }

        public TimeSpan GetIdleTimeoutForTests() => GetIdleTimeout();
    }
}
