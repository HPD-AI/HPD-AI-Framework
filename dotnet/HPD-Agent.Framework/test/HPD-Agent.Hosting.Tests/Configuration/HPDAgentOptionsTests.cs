using FluentAssertions;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Hosting.Packages;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Packages;
using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Hosting.Tests.Configuration;

/// <summary>
/// Tests for HPDAgentConfig configuration.
/// </summary>
public class HPDAgentConfigTests
{
    [Fact]
    public void SessionStore_TakesPriority_OverSessionStorePath()
    {
        // Arrange
        var customStore = new InMemorySessionStore();
        var options = new HPDAgentConfig
        {
            SessionStore = customStore,
            SessionStorePath = "./some-path" // Should be ignored
        };

        // Assert
        options.SessionStore.Should().BeSameAs(customStore);
        options.SessionStorePath.Should().Be("./some-path"); // Still set, but not used
    }

    [Fact]
    public void DefaultAgent_TakesPriority_OverDefaultAgentPath()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "Test Agent",
            SystemInstructions = "Test instructions"
        };

        var options = new HPDAgentConfig
        {
            DefaultAgent = config,
            DefaultAgentPath = "./config.json" // Should be ignored
        };

        // Assert
        options.DefaultAgent.Should().BeSameAs(config);
        options.DefaultAgentPath.Should().Be("./config.json"); // Still set, but not used
    }

    [Fact]
    public void AgentContributors_CanBeAddedAndInvoked()
    {
        // Arrange
        var callbackCalled = false;
        var options = new HPDAgentConfig();
        using var services = new ServiceCollection().BuildServiceProvider();
        options.AgentContributors.Add(new DelegateAgentBuilderContributor(_ => { callbackCalled = true; }));

        // Act
        options.AgentContributors[0].ConfigureAgent(
            new AgentBuilder(),
            new HpdAgentContributionContext
            {
                Owner = HpdContributionOwner.App,
                Services = services,
                AgentId = "agent"
            });

        // Assert
        callbackCalled.Should().BeTrue();
    }

    [Fact]
    public void AgentContributors_RecordOwnersAndCanRemoveByOwner()
    {
        var packageOwner = new HpdContributionOwner("hpd.test.package", "test");
        var options = new HPDAgentConfig();

        options.AgentContributors.Add(
            "test.agent",
            new DelegateAgentBuilderContributor(_ => { }),
            packageOwner);

        options.AgentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Key == "test.agent" &&
            contribution.Owner == packageOwner);
        options.AgentContributors.Owners.Should().ContainSingle().Which.Should().Be(packageOwner);

        options.AgentContributors.RemoveOwner(packageOwner).Should().BeTrue();

        options.AgentContributors.Count.Should().Be(0);
    }

    [Fact]
    public void PackageContributions_UseHostedBackendStores()
    {
        var options = new HPDAgentConfig();
        var packages = options.CreatePackageManager(new ServiceCollection());

        packages.Enable(new BackendPackage());

        options.PackageContributions.AgentContributors.Should().BeSameAs(options.AgentContributors);
        options.PackageContributions.ProviderContributions.Should().BeSameAs(options.ProviderContributions);
        options.AgentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Key == "test.agent");
        options.ProviderContributions.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Key == "test.provider");
    }

    [Fact]
    public void DefaultIdleTimeout_Is30Minutes()
    {
        // Arrange
        var options = new HPDAgentConfig();

        // Assert
        options.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void AgentIdleTimeout_CanBeCustomized()
    {
        // Arrange
        var options = new HPDAgentConfig
        {
            AgentIdleTimeout = TimeSpan.FromMinutes(60)
        };

        // Assert
        options.AgentIdleTimeout.Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void AllProperties_CanBeSetToNull()
    {
        // Arrange
        var options = new HPDAgentConfig
        {
            SessionStore = null,
            SessionStorePath = null,
            DefaultAgent = null,
            DefaultAgentPath = null
        };

        // Assert - Should not throw
        options.SessionStore.Should().BeNull();
        options.SessionStorePath.Should().BeNull();
        options.DefaultAgent.Should().BeNull();
        options.DefaultAgentPath.Should().BeNull();
        options.AgentContributors.Count.Should().Be(0);
        options.ProviderContributions.ProviderFactories.Should().BeEmpty();
    }

    private sealed class BackendPackage : HpdPackage
    {
        public override HpdPackageManifest Manifest { get; } = new("hpd.test.backend", "Backend Test", new Version(1, 0));

        public override void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor("test.agent", new DelegateAgentBuilderContributor(_ => { }));
            builder.AddProviderContributor(new TestProviderContributor());
        }
    }

    private sealed class TestProviderContributor : IProviderContributor
    {
        public void ConfigureProviders(
            IProviderContributionBuilder builder,
            HpdProviderContributionContext context) =>
            builder.AddProviderFactory("test.provider", _ => new TestProvider());
    }

    private sealed class TestProvider : IProvider
    {
        public string ProviderKey => "test.provider";

        public string DisplayName => "Test Provider";

        public IProviderErrorHandler CreateErrorHandler() => new TestErrorHandler();

        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName
        };

        public ProviderValidationResult ValidateConfiguration(
            ClientProviderConfig config,
            ProviderClientFamily family) =>
            ProviderValidationResult.Success();
    }

    private sealed class TestErrorHandler : IProviderErrorHandler
    {
        public ProviderErrorDetails? ParseError(Exception exception) => null;

        public TimeSpan? GetRetryDelay(
            ProviderErrorDetails details,
            int attempt,
            TimeSpan initialDelay,
            double multiplier,
            TimeSpan maxDelay) => null;

        public bool RequiresSpecialHandling(ProviderErrorDetails details) => false;
    }
}
