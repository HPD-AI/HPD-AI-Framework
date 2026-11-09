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
        _sessionStore = new InMemorySessionStore(HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec);
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
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = InjectTestProvider;

        var manager = MakeManager();
        await manager.EnsureConfiguredDefinitionAsync("configured", CancellationToken.None);
        var stored = (await _agentStore.LoadAsync("configured"))!;

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);
        agent.Config!.Name.Should().Be("Default Agent");
    }

    [Fact]
    public async Task BuildAgentAsync_UsesDefaultAgentPath_WhenProvided()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
        var config = MakeConfig("FileConfig Agent");
        await File.WriteAllTextAsync(
            tempPath,
            HPD.Agent.Serialization.HpdAgentConfigSerializer.Serialize(config));

        _optionsMonitor.CurrentValue.DefaultAgentPath = tempPath;
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = InjectTestProvider;

        try
        {
            var manager = MakeManager();
            await manager.EnsureConfiguredDefinitionAsync("file-config", CancellationToken.None);
            var stored = (await _agentStore.LoadAsync("file-config"))!;
            var agent = await manager.GetOrBuildAgentAsync(stored.Id);
            agent.Config!.Name.Should().Be("FileConfig Agent");
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task BuildAgentAsync_UsesDeferredDefaultAgentDocument_WhenProvided()
    {
        _optionsMonitor.CurrentValue.DefaultAgentDocument = """
            { "name": "Configured Agent", "systemInstructions": "Configured at runtime." }
            """;
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = InjectTestProvider;

        var manager = MakeManager();
        await manager.EnsureConfiguredDefinitionAsync("deferred", CancellationToken.None);
        var stored = (await _agentStore.LoadAsync("deferred"))!;

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);

        agent.Config!.Name.Should().Be("Configured Agent");
        agent.Config.SystemInstructions.Should().Be("Configured at runtime.");
    }

    [Fact]
    public async Task BuildAgentAsync_Builds_ExplicitEmptyDefinition()
    {
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = InjectTestProvider;

        var manager = MakeManager();
        var stored = await manager.CreateDefinitionAsync(new AgentConfig(), "Empty", agentId: "empty");

        var agent = await manager.GetOrBuildAgentAsync(stored.Id);
        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildAgentAsync_ConfigureEachAgentBuilderRunsAfterDefinitionAndCanOverride()
    {
        _optionsMonitor.CurrentValue.DefaultAgent = MakeConfig("X");
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = builder =>
        {
            builder.WithInstructions("Overridden by host");
            InjectTestProvider(builder);
        };

        var manager = MakeManager();
        await manager.EnsureConfiguredDefinitionAsync("customized", CancellationToken.None);
        var agent = await manager.GetOrBuildAgentAsync("customized");

        agent.Config!.Name.Should().Be("X");
        agent.Config.SystemInstructions.Should().Be("Overridden by host");
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_CachesInstance_ByAgentId()
    {
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = InjectTestProvider;
        var manager = MakeManager();
        var stored = await SeedDefault(manager);

        var a1 = await manager.GetOrBuildAgentAsync(stored.Id);
        var a2 = await manager.GetOrBuildAgentAsync(stored.Id);

        a1.Should().BeSameAs(a2);
    }

    [Fact]
    public async Task GetOrBuildAgentAsync_Throws_WhenDefinitionMissing()
    {
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = InjectTestProvider;
        var manager = MakeManager();

        var action = () => manager.GetOrBuildAgentAsync("no-such-agent");

        await action.Should().ThrowAsync<KeyNotFoundException>();
        (await _agentStore.LoadAsync("no-such-agent")).Should().BeNull();
    }

    [Fact]
    public async Task DeletedDeclaredDefinition_IsNotRecreatedByRuntimeResolution()
    {
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = InjectTestProvider;
        var manager = MakeManager();
        await manager.EnsureConfiguredDefinitionAsync("declared", CancellationToken.None);
        await manager.DeleteDefinitionAsync("declared");

        var action = () => manager.GetOrBuildAgentAsync("declared");

        await action.Should().ThrowAsync<KeyNotFoundException>();
        (await _agentStore.LoadAsync("declared")).Should().BeNull();
    }

    [Fact]
    public async Task BuildAgentAsync_UsesStoredToolHarnessConfig()
    {
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = builder =>
        {
            builder.WithToolHarnessCatalogFrom<CodingToolHarness>()
                .WithPermissions(HPD.Agent.Permissions.AuthorizationMode.Bypass);
            InjectTestProvider(builder);
        };
        var manager = MakeManager();
        var stored = await manager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "Coding",
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } },
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
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = builder =>
            builder.WithToolHarnessCatalogFrom<CodingToolHarness>()
                .WithPermissions(HPD.Agent.Permissions.AuthorizationMode.Bypass);
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
        _optionsMonitor.CurrentValue.ConfigureEachAgentBuilder = InjectTestProviderRegistry;
        var manager = MakeManager();
        var stored = await manager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "Runtime Summarizing",
            Compaction = new CompactionConfig
            {
                Automatic = new AutomaticCompactionPolicy
                {
                    Trigger = new TurnCountCompactionTrigger(10),
                    Compaction = new CompactionSpecification
                    {
                        Point = new CompactAtCurrentHead(),
                        Strategy = new SummarizingCompaction
                        {
                            Summarizer = new ChatClientConfig {
                                Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" },
                                ModelName = "summarizer-model"
                            }
                        }
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

    private static async Task<StoredAgent> SeedDefault(AgentManager manager)
    {
        return await manager.CreateDefinitionAsync(new AgentConfig
        {
            Name = "Default",
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } }
        }, "Default");
    }

    private static AgentConfig MakeConfig(string name) => new AgentConfig
    {
        Name = name,
        Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } }
    };

    private static void InjectTestProvider(AgentBuilder builder)
    {
        builder.Config.SetChatClientConfig(new ChatClientConfig
        {
            Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" },
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

    // ──────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ──────────────────────────────────────────────────────────────────────────

    private class OptionsMonitorWrapper : IOptionsMonitor<HPDAgentHostOptions>
    {
        public HPDAgentHostOptions CurrentValue { get; } = new HPDAgentHostOptions();
        public HPDAgentHostOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<HPDAgentHostOptions, string?> listener) => null;
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
            EventComposition = HPD.Agent.AspNetCore.Tests.TestEventApplication.Composition,
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig { Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" }, ModelName = "test-model" } }
        };
    }

    /// <summary>Subclass that exposes the protected GetIdleTimeout for testing.</summary>
    private class TestableAgentManager : AspNetCoreAgentManager
    {
        public TestableAgentManager(
            IAgentStore agentStore,
            AspNetCoreSessionManager sessionManager,
            IOptionsMonitor<HPDAgentHostOptions> optionsMonitor,
            IServiceProvider serviceProvider,
            string name,
            IAgentFactory? agentFactory = null)
            : base(
                agentStore,
                sessionManager,
                optionsMonitor,
                serviceProvider,
                name,
                new InMemoryContentStore(),
                agentFactory,
                HPD.Agent.AspNetCore.Tests.TestEventApplication.Composition) { }

        public TimeSpan GetIdleTimeoutForTests() => GetIdleTimeout();
    }
}
