using FluentAssertions;
using HPD.Agent.AspNetCore;
using HPD.Agent.AspNetCore.DependencyInjection;
using HPD.Agent.Hosting.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Unit tests for HPD-Agent service registration (AddHPDAgent).
/// Tests DI configuration and multi-agent scenarios.
/// </summary>
public class ServiceRegistrationTests
{
    #region AddHPDAgent

    [Fact]
    public void AddHPDAgent_RegistersAgentRegistry_AsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHPDAgent();
        var provider = services.BuildServiceProvider();

        // Assert
        var registry1 = provider.GetService<HPDAgentRegistry>();
        var registry2 = provider.GetService<HPDAgentRegistry>();
        registry1.Should().NotBeNull();
        registry1.Should().BeSameAs(registry2);
    }

    [Fact]
    public void AddHPDAgent_RegistersNamedOptions_Correctly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHPDAgent(opts =>
        {
            opts.AgentIdleTimeout = TimeSpan.FromMinutes(60);
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptionsMonitor<HPDAgentConfig>>();
        options.Should().NotBeNull();
        options!.CurrentValue.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void AddHPDAgent_RegistersJsonOptions_ForAOT()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHPDAgent();
        var provider = services.BuildServiceProvider();

        // Assert - JSON options configurator should be registered
        // This is internal implementation detail but verifies AOT compatibility
        var configurators = provider.GetServices<IConfigureOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        configurators.Should().NotBeEmpty();
    }

    [Fact]
    public void AddHPDAgent_AllowsMultipleNamedAgents()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHPDAgent("agent1", opts =>
        {
            opts.AgentIdleTimeout = TimeSpan.FromMinutes(30);
        });
        services.AddHPDAgent("agent2", opts =>
        {
            opts.AgentIdleTimeout = TimeSpan.FromMinutes(60);
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<HPDAgentConfig>>();
        var options1 = optionsMonitor.Get("agent1");
        var options2 = optionsMonitor.Get("agent2");

        options1.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
        options2.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void AddHPDAgent_DefaultName_UsesOptionsDefaultName()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddHPDAgent(opts =>
        {
            opts.SessionStorePath = "./test";
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<HPDAgentConfig>>();
        var defaultOptions = optionsMonitor.Get(Options.DefaultName);
        defaultOptions.SessionStorePath.Should().Be("./test");
    }

    [Fact]
    public void AddHPDAgent_CanBeCalledMultipleTimes()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - Should not throw
        services.AddHPDAgent("agent1");
        services.AddHPDAgent("agent2");
        services.AddHPDAgent("agent3");
        var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetRequiredService<HPDAgentRegistry>();
        registry.Should().NotBeNull();
    }

    [Fact]
    public void AddHPDAgent_SupportsIAgentFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IAgentFactory, TestAgentFactory>();

        // Act
        services.AddHPDAgent();
        var provider = services.BuildServiceProvider();

        // Assert
        var factory = provider.GetService<IAgentFactory>();
        factory.Should().NotBeNull();
        factory.Should().BeOfType<TestAgentFactory>();
    }

    #endregion

    #region Test Helpers

    private class TestAgentFactory : IAgentFactory
    {
        public Task<HPD.Agent.Agent> CreateAgentAsync(
            string sessionId,
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
