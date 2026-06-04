using FluentAssertions;
using HPD.Agent.AspNetCore;
using HPD.Agent;
using HPD.Agent.AspNetCore.Tests.TestInfrastructure;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Tests for HPDAgentRegistry (via DI) — verifies that AddHPDAgent registers
/// a singleton AgentManager and SessionManager, seeds the "default" StoredAgent,
/// and correctly isolates named registrations.
/// </summary>
public class HPDAgentRegistryTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // AgentManager and SessionManager are registered
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAgent_RegistersAgentManager_AsSingleton()
    {
        var sp = BuildProvider();

        var am1 = sp.GetService<AgentManager>();
        var am2 = sp.GetService<AgentManager>();

        am1.Should().NotBeNull();
        am1.Should().BeSameAs(am2);
    }

    [Fact]
    public void AddHPDAgent_RegistersSessionManager_AsSingleton()
    {
        var sp = BuildProvider();

        var sm1 = sp.GetService<SessionManager>();
        var sm2 = sp.GetService<SessionManager>();

        sm1.Should().NotBeNull();
        sm1.Should().BeSameAs(sm2);
    }

    [Fact]
    public void AddHPDAgent_SessionManager_HasRepository()
    {
        var sp = BuildProvider();
        var sm = sp.GetRequiredService<SessionManager>();
        sm.Repository.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Named options isolation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAgent_NamedOptions_AreIsolated()
    {
        var services = new ServiceCollection();
        services.AddHPDAgent("agent1", opts => opts.AgentIdleTimeout = TimeSpan.FromMinutes(30));
        services.AddHPDAgent("agent2", opts => opts.AgentIdleTimeout = TimeSpan.FromMinutes(60));
        var sp = services.BuildServiceProvider();

        var monitor = sp.GetRequiredService<IOptionsMonitor<HPDAgentConfig>>();
        monitor.Get("agent1").AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
        monitor.Get("agent2").AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(60));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Store selection
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AddHPDAgent_CreatesWorkspaceBackedSessionRepository_WhenNoneProvided()
    {
        var sp = BuildProvider();
        var sm = sp.GetRequiredService<SessionManager>();
        sm.Repository.Should().BeOfType<WorkspaceSessionRepository>();
    }

    [Fact]
    public void AddHPDAgent_UsesWorkspaceStorePathAsJsonWorkspacePath()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var sp = BuildProvider(opts => opts.WorkspaceStorePath = tempPath);
            var sm = sp.GetRequiredService<SessionManager>();

            sm.Repository.Should().BeOfType<WorkspaceSessionRepository>();
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
    public void AddHPDAgent_ResolvesIAgentFactory_FromDI()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentFactory, StubAgentFactory>();
        services.AddHPDAgent(opts => opts.UseInMemoryWorkspace());
        var sp = services.BuildServiceProvider();

        // AgentManager and SessionManager must resolve without throwing
        sp.GetService<AgentManager>().Should().NotBeNull();
        sp.GetService<SessionManager>().Should().NotBeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // "default" StoredAgent created on first build
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddHPDAgent_DoesNotSeedDefaultStoredAgent_OnRegistration()
    {
        var workspace = new InMemoryWorkspaceStore();
        var repository = new WorkspaceAgentRepository(workspace);
        var sp = BuildProvider(opts => opts.UseWorkspaceStore(workspace));

        // Trigger pair creation by resolving AgentManager
        _ = sp.GetRequiredService<AgentManager>();

        var def = await repository.LoadAsync("default");
        def.Should().BeNull("registration should no longer fire-and-forget seed a default definition");
    }

    [Fact]
    public async Task AddHPDAgent_PersistsDefaultStoredAgent_OnFirstBuild()
    {
        var workspace = new InMemoryWorkspaceStore();
        var repository = new WorkspaceAgentRepository(workspace);
        var sp = BuildProvider(opts =>
        {
            opts.UseWorkspaceStore(workspace);
            opts.ConfigureAgent = InjectTestProvider;
        });

        var manager = sp.GetRequiredService<AgentManager>();

        await manager.GetOrBuildAgentAsync("default");

        var def = await repository.LoadAsync("default");
        def.Should().NotBeNull("first build should persist the synthesized default definition");
        def!.Id.Should().Be("default");
    }

    [Fact]
    public async Task AddHPDAgent_DoesNotOverwrite_ExistingDefaultAgent_OnFirstBuild()
    {
        var workspace = new InMemoryWorkspaceStore();
        var repository = new WorkspaceAgentRepository(workspace);
        var existing = new StoredAgent
        {
            Id = "default",
            Name = "Pre-existing",
            Config = new AgentConfig(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await repository.SaveAsync(existing);

        var sp = BuildProvider(opts =>
        {
            opts.UseWorkspaceStore(workspace);
            opts.ConfigureAgent = InjectTestProvider;
        });

        var manager = sp.GetRequiredService<AgentManager>();
        await manager.GetOrBuildAgentAsync("default");

        var loaded = await repository.LoadAsync("default");
        loaded!.Name.Should().Be("Pre-existing");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(Action<HPDAgentConfig>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddHPDAgent(opts =>
        {
            configure?.Invoke(opts);
        });
        return services.BuildServiceProvider();
    }

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

    private sealed class StubAgentFactory : IAgentFactory
    {
        public Task<Agent> CreateAgentAsync(string agentId, ISessionRepository sessionRepository, CancellationToken ct = default)
            => throw new NotSupportedException("Stub — not called in this test.");
    }
}
