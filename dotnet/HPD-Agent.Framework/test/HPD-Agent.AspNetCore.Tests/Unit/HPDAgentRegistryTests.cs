using FluentAssertions;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.AspNetCore;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Tests for HPDAgentRegistry (via DI) — verifies that AddHPDAgentHost registers
/// a singleton AgentManager and SessionManager, seeds the "default" StoredAgent,
/// and correctly isolates named registrations.
/// </summary>
public class HPDAgentRegistryTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // AgentManager and SessionManager are registered
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAgentHost_RegistersAgentManager_AsSingleton()
    {
        var sp = BuildProvider();

        var am1 = sp.GetService<AgentManager>();
        var am2 = sp.GetService<AgentManager>();

        am1.Should().NotBeNull();
        am1.Should().BeSameAs(am2);
    }

    [Fact]
    public void AddHPDAgentHost_RegistersSessionManager_AsSingleton()
    {
        var sp = BuildProvider();

        var sm1 = sp.GetService<SessionManager>();
        var sm2 = sp.GetService<SessionManager>();

        sm1.Should().NotBeNull();
        sm1.Should().BeSameAs(sm2);
    }

    [Fact]
    public void AddHPDAgentHost_SessionManager_HasStore()
    {
        var sp = BuildProvider();
        var sm = sp.GetRequiredService<SessionManager>();
        sm.Store.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Named options isolation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAgentHost_NamedOptions_AreIsolated()
    {
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();
        services.AddTestApplicationCompositions().AddHPDAgentHost("agent1", opts => opts.AgentIdleTimeout = TimeSpan.FromMinutes(30));
        services.AddTestApplicationCompositions().AddHPDAgentHost("agent2", opts => opts.AgentIdleTimeout = TimeSpan.FromMinutes(60));
        var sp = services.BuildServiceProvider();

        var monitor = sp.GetRequiredService<IOptionsMonitor<HPDAgentHostOptions>>();
        monitor.Get("agent1").AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
        monitor.Get("agent2").AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(60));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Store selection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAgentHost_UsesProvidedSessionStore()
    {
        var customStore = new InMemorySessionStore(HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec);
        var sp = BuildProvider(opts => opts.SessionStore = customStore);

        var sm = sp.GetRequiredService<SessionManager>();
        sm.Store.Should().BeSameAs(customStore);
    }

    [Fact]
    public void AddHPDAgentHost_CreatesInMemorySessionStore_WhenNoneProvided()
    {
        var sp = BuildProvider();
        var sm = sp.GetRequiredService<SessionManager>();
        sm.Store.Should().BeOfType<InMemorySessionStore>();
    }

    [Fact]
    public void AddHPDAgentHost_CreatesFileSessionStore_WhenPathProvided()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var sp = BuildProvider(opts => opts.SessionStore = new FileSessionStore(tempPath, HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec));
            var sm = sp.GetRequiredService<SessionManager>();
            sm.Store.Should().BeOfType<FileSessionStore>();
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IAgentFactory wiring
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAgentHost_ResolvesIAgentFactory_FromDI()
    {
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();
        services.AddSingleton<IAgentFactory, StubAgentFactory>();
        services.AddTestApplicationCompositions().AddHPDAgentHost(opts => opts.SessionStore = new InMemorySessionStore(HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec));
        var sp = services.BuildServiceProvider();

        // AgentManager and SessionManager must resolve without throwing
        sp.GetService<AgentManager>().Should().NotBeNull();
        sp.GetService<SessionManager>().Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // "default" StoredAgent created on first build
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddHPDAgentHost_DoesNotSeedDefaultStoredAgent_OnRegistration()
    {
        var agentStore = new InMemoryAgentStore();
        var sp = BuildProvider(opts => opts.AgentStore = agentStore);

        // Trigger pair creation by resolving AgentManager
        _ = sp.GetRequiredService<AgentManager>();

        var def = await agentStore.LoadAsync("default");
        def.Should().BeNull("registration should no longer fire-and-forget seed a default definition");
    }

    [Fact]
    public async Task HostDefinitionBootstrap_CreatesDeclaredAgent_WithoutChangingItDuringBuild()
    {
        var agentStore = new InMemoryAgentStore();
        var sp = BuildProvider(opts =>
        {
            opts.AgentStore = agentStore;
            opts.ConfigureEachAgentBuilder = InjectTestProvider;
        });

        var manager = (AspNetCoreAgentManager)sp.GetRequiredService<AgentManager>();

        await manager.EnsureConfiguredDefinitionAsync("default", CancellationToken.None);
        var beforeBuild = await agentStore.LoadAsync("default");
        await manager.GetOrBuildAgentAsync("default");

        var afterBuild = await agentStore.LoadAsync("default");
        beforeBuild.Should().NotBeNull();
        afterBuild.Should().NotBeNull();
        afterBuild!.UpdatedAt.Should().Be(beforeBuild!.UpdatedAt);
    }

    [Fact]
    public async Task HostDefinitionBootstrap_DoesNotOverwrite_ExistingDefinition()
    {
        var agentStore = new InMemoryAgentStore();
        var existing = new StoredAgent
        {
            Id = "default",
            Name = "Pre-existing",
            Config = new AgentConfig(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await agentStore.SaveAsync(existing);

        var sp = BuildProvider(opts =>
        {
            opts.AgentStore = agentStore;
            opts.ConfigureEachAgentBuilder = InjectTestProvider;
        });

        var manager = (AspNetCoreAgentManager)sp.GetRequiredService<AgentManager>();
        await manager.EnsureConfiguredDefinitionAsync("default", CancellationToken.None);

        var loaded = await agentStore.LoadAsync("default");
        loaded!.Name.Should().Be("Pre-existing");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(Action<HPDAgentHostOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();
        services.AddTestApplicationCompositions().AddHPDAgentHost(opts =>
        {
            opts.SessionStore = new InMemorySessionStore(HPD.Agent.AspNetCore.Tests.TestEventApplication.Codec);
            configure?.Invoke(opts);
        });
        return services.BuildServiceProvider();
    }

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

    private sealed class StubAgentFactory : IAgentFactory
    {
        public Task<Agent> CreateAgentAsync(string agentId, ISessionStore store, CancellationToken ct = default)
            => throw new NotSupportedException("Stub — not called in this test.");
    }
}
