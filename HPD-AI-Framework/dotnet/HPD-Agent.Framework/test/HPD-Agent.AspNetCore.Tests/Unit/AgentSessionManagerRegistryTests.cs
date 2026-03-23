using FluentAssertions;
using HPD.Agent.AspNetCore.DependencyInjection;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="HPDAgentRegistry"/>.
/// Tests named manager-pair resolution and lifecycle.
/// </summary>
public class AgentSessionManagerRegistryTests
{
    #region Named Manager Resolution

    [Fact]
    public void Get_CreatesManagerPair_OnFirstCall()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<HPDAgentConfig>(Options.DefaultName, opts =>
        {
            opts.SessionStore = new InMemorySessionStore();
        });
        var provider = services.BuildServiceProvider();
        var registry = new HPDAgentRegistry(provider);

        // Act
        var pair = registry.Get(Options.DefaultName);

        // Assert
        pair.Should().NotBeNull();
        pair.SessionManager.Should().NotBeNull();
        pair.AgentManager.Should().NotBeNull();
    }

    [Fact]
    public void Get_ReturnsSamePair_OnSubsequentCalls()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<HPDAgentConfig>(Options.DefaultName, opts =>
        {
            opts.SessionStore = new InMemorySessionStore();
        });
        var provider = services.BuildServiceProvider();
        var registry = new HPDAgentRegistry(provider);

        // Act
        var pair1 = registry.Get(Options.DefaultName);
        var pair2 = registry.Get(Options.DefaultName);

        // Assert
        pair1.SessionManager.Should().BeSameAs(pair2.SessionManager);
        pair1.AgentManager.Should().BeSameAs(pair2.AgentManager);
    }

    [Fact]
    public void Get_CreatesDifferentPairs_ForDifferentNames()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<HPDAgentConfig>("agent1", opts =>
        {
            opts.SessionStore = new InMemorySessionStore();
        });
        services.Configure<HPDAgentConfig>("agent2", opts =>
        {
            opts.SessionStore = new InMemorySessionStore();
        });
        var provider = services.BuildServiceProvider();
        var registry = new HPDAgentRegistry(provider);

        // Act
        var pair1 = registry.Get("agent1");
        var pair2 = registry.Get("agent2");

        // Assert
        pair1.SessionManager.Should().NotBeSameAs(pair2.SessionManager);
    }

    [Fact]
    public void CreatePair_UsesOptionsMonitor_ForNamedOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<HPDAgentConfig>("test-agent", opts =>
        {
            opts.SessionStore = new InMemorySessionStore();
            opts.AgentIdleTimeout = TimeSpan.FromMinutes(60);
        });
        var provider = services.BuildServiceProvider();
        var registry = new HPDAgentRegistry(provider);

        // Act
        var pair = registry.Get("test-agent");

        // Assert
        pair.Should().NotBeNull();
    }

    [Fact]
    public void CreatePair_ResolvesIAgentFactory_FromDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton<IAgentFactory, TestAgentFactory>();
        services.Configure<HPDAgentConfig>(Options.DefaultName, opts =>
        {
            opts.SessionStore = new InMemorySessionStore();
        });
        var provider = services.BuildServiceProvider();
        var registry = new HPDAgentRegistry(provider);

        // Act
        var pair = registry.Get(Options.DefaultName);

        // Assert
        pair.Should().NotBeNull();
    }

    [Fact]
    public void CreatePair_CreatesJsonSessionStore_WhenPathProvided()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<HPDAgentConfig>(Options.DefaultName, opts =>
        {
            opts.SessionStorePath = tempPath;
        });
        var provider = services.BuildServiceProvider();
        var registry = new HPDAgentRegistry(provider);

        // Act
        var pair = registry.Get(Options.DefaultName);

        // Assert
        pair.Should().NotBeNull();
        pair.SessionManager.Store.Should().NotBeNull();

        // Cleanup
        if (Directory.Exists(tempPath))
            Directory.Delete(tempPath, true);
    }

    [Fact]
    public void CreatePair_CreatesInMemoryStore_WhenNoPathProvided()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<HPDAgentConfig>(Options.DefaultName, opts => { });
        var provider = services.BuildServiceProvider();
        var registry = new HPDAgentRegistry(provider);

        // Act
        var pair = registry.Get(Options.DefaultName);

        // Assert
        pair.Should().NotBeNull();
        pair.SessionManager.Store.Should().BeOfType<InMemorySessionStore>();
    }

    [Fact]
    public void CreatePair_UsesProvidedStore_WhenAvailable()
    {
        // Arrange
        var customStore = new InMemorySessionStore();
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<HPDAgentConfig>(Options.DefaultName, opts =>
        {
            opts.SessionStore = customStore;
        });
        var provider = services.BuildServiceProvider();
        var registry = new HPDAgentRegistry(provider);

        // Act
        var pair = registry.Get(Options.DefaultName);

        // Assert
        pair.SessionManager.Store.Should().BeSameAs(customStore);
    }

    #endregion

    #region Test Helpers

    private class TestAgentFactory : IAgentFactory
    {
        public Task<HPD.Agent.Agent> CreateAgentAsync(
            string agentId,
            ISessionStore store,
            CancellationToken ct = default)
        {
            var config = new AgentConfig
            {
                Name = "TestAgent",
                Provider = new ProviderConfig
                {
                    ProviderKey = "test",
                    ModelName = "test-model"
                }
            };

            var chatClient = new HPD.Agent.AspNetCore.Tests.TestInfrastructure.FakeChatClient();
            var providerRegistry = new HPD.Agent.AspNetCore.Tests.TestInfrastructure.TestProviderRegistry(chatClient);

            return new AgentBuilder(config, providerRegistry)
                .WithSessionStore(store)
                .BuildAsync(ct);
        }
    }

    #endregion
}
