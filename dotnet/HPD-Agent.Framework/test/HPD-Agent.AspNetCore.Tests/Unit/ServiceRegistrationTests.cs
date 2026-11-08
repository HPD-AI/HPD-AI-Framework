using FluentAssertions;
using HPD.Agent.AspNetCore;
using HPD.Agent.AspNetCore.DependencyInjection;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Providers;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Tests.Unit;

/// <summary>
/// Unit tests for HPD-Agent service registration (AddHPDAgentHost).
/// Tests DI configuration and multi-agent scenarios.
/// </summary>
public class ServiceRegistrationTests
{
    #region AddHPDAgentHost

    [Fact]
    public void AddHPDAgentHost_RegistersAgentRegistry_AsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();

        // Act
        services.AddTestApplicationCompositions().AddHPDAgentHost();
        var provider = services.BuildServiceProvider();

        // Assert
        var registry1 = provider.GetService<HPDAgentRegistry>();
        var registry2 = provider.GetService<HPDAgentRegistry>();
        registry1.Should().NotBeNull();
        registry1.Should().BeSameAs(registry2);
    }

    [Fact]
    public void AddHPDAgentHost_RegistersNamedOptions_Correctly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();

        // Act
        services.AddTestApplicationCompositions().AddHPDAgentHost(opts =>
        {
            opts.AgentIdleTimeout = TimeSpan.FromMinutes(60);
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<IOptionsMonitor<HPDAgentHostOptions>>();
        options.Should().NotBeNull();
        options!.CurrentValue.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void AddHPDAgentHost_RegistersJsonOptions_ForAOT()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();

        // Act
        services.AddTestApplicationCompositions().AddHPDAgentHost();
        var provider = services.BuildServiceProvider();

        // Assert - JSON options configurator should be registered
        // This is internal implementation detail but verifies AOT compatibility
        var configurators = provider.GetServices<IConfigureOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        configurators.Should().NotBeEmpty();
    }

    [Fact]
    public void AddHPDAgentHost_WithProviderComposition_InstallsSharedRunConfigConverter()
    {
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();
        services.AddSingleton(ProviderComposition.Create([]));
        services.AddTestApplicationCompositions().AddHPDAgentHost();
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<JsonOptions>>().Value;

        options.SerializerOptions.Converters.Should().ContainSingle(converter =>
            converter.GetType().Name == "AgentRunConfigJsonConverter");
    }

    [Fact]
    public void AddHPDAgentHost_AllowsMultipleNamedAgents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();

        // Act
        services.AddTestApplicationCompositions().AddHPDAgentHost("agent1", opts =>
        {
            opts.AgentIdleTimeout = TimeSpan.FromMinutes(30);
        });
        services.AddTestApplicationCompositions().AddHPDAgentHost("agent2", opts =>
        {
            opts.AgentIdleTimeout = TimeSpan.FromMinutes(60);
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<HPDAgentHostOptions>>();
        var options1 = optionsMonitor.Get("agent1");
        var options2 = optionsMonitor.Get("agent2");

        options1.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
        options2.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void AddHPDAgentHost_DefaultName_UsesOptionsDefaultName()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();

        // Act
        services.AddTestApplicationCompositions().AddHPDAgentHost(opts =>
        {
            opts.SessionStorePath = "./test";
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<HPDAgentHostOptions>>();
        var defaultOptions = optionsMonitor.Get(Options.DefaultName);
        defaultOptions.SessionStorePath.Should().Be("./test");
    }

    [Fact]
    public void AddHPDAgentHostFromConfigFile_LoadsYamlDefaultAgent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpd-host-agent-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, """
        name: Hosted YAML Agent
        systemInstructions: Run from hosting config.
        maxAgenticIterations: 7
        """);

        try
        {
            var services = new ServiceCollection();
        services.AddTestApplicationCompositions();

            services.AddTestApplicationCompositions().AddHPDAgentHostFromConfigFile("hosted", path, opts =>
            {
                opts.AgentIdleTimeout = TimeSpan.FromMinutes(5);
            });
            var provider = services.BuildServiceProvider();

            var options = provider.GetRequiredService<IOptionsMonitor<HPDAgentHostOptions>>().Get("hosted");
            options.DefaultAgent.Should().BeNull();
            options.DefaultAgentPath.Should().Be(path);
            options.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(5));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AddHPDAgentHostFromConfiguration_LoadsDefaultAgentWithoutBinderReflection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["agent:name"] = "Hosted IConfiguration Agent",
                ["agent:systemInstructions"] = "Run from IConfiguration.",
                ["agent:toolharnesses:0"] = "CodingToolHarness"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();

        services.AddTestApplicationCompositions().AddHPDAgentHostFromConfiguration("configured", configuration.GetSection("agent"));
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<HPDAgentHostOptions>>().Get("configured");
        options.DefaultAgent.Should().BeNull();
        options.DefaultAgentDocument.Should().Contain("Hosted IConfiguration Agent");
        options.DefaultAgentDocument.Should().Contain("CodingToolHarness");
    }

    [Fact]
    public void AddHPDAgentHost_CanBeCalledMultipleTimes()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();

        // Act - Should not throw
        services.AddTestApplicationCompositions().AddHPDAgentHost("agent1");
        services.AddTestApplicationCompositions().AddHPDAgentHost("agent2");
        services.AddTestApplicationCompositions().AddHPDAgentHost("agent3");
        var provider = services.BuildServiceProvider();

        // Assert
        var registry = provider.GetRequiredService<HPDAgentRegistry>();
        registry.Should().NotBeNull();
    }

    [Fact]
    public void AddHPDAgentHost_SupportsIAgentFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestApplicationCompositions();
        services.AddSingleton<IAgentFactory, TestAgentFactory>();

        // Act
        services.AddTestApplicationCompositions().AddHPDAgentHost();
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
                Clients = new AgentClientsConfig { Chat = new ChatClientConfig {
                    Provider = new HPD.Agent.Providers.ProviderReference { Key = "test" },
                    ModelName = "test-model"
                } }
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
