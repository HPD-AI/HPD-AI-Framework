using FluentAssertions;
using HPD.Agent.Hosting.Configuration;
using HPD.Agent.Packages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HPD.Agent.AspNetCore.Packages.Tests;

public sealed class HpdAspNetCorePackageRuntimeTests
{
    [Fact]
    public void AddHPDAgentPackageManagement_RegistersRuntime()
    {
        var services = new ServiceCollection();

        services.AddHPDAgentPackageManagement();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<HpdAspNetCorePackageRuntime>()
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void Runtime_UsesHostedPackageContributionStores()
    {
        var services = new ServiceCollection();
        services.Configure<HPDAgentConfig>(options =>
        {
            options.DefaultAgent = new AgentConfig { Name = "Test" };
        });
        services.AddHPDAgentPackageManagement();
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<HpdAspNetCorePackageRuntime>();
        var config = provider.GetRequiredService<IOptionsMonitor<HPDAgentConfig>>()
            .CurrentValue;

        runtime.Packages.Enable(new TestPackage("hpd.test.aspnetcore.packages.runtime"));

        config.AgentContributors.Contributions.Should().ContainSingle(contribution =>
            contribution.Key == "test.agent");
        runtime.List().Packages.Should().ContainSingle(package =>
            package.Id == "hpd.test.aspnetcore.packages.runtime" &&
            package.Contributions.AgentContributors.Contains("test.agent"));
    }

    [Fact]
    public void Runtime_CanEnableRegisteredPackageById()
    {
        const string packageId = "hpd.test.aspnetcore.packages.registered";
        HpdPackageRegistry.Register(new TestPackage(packageId));
        var services = new ServiceCollection();
        services.AddHPDAgentPackageManagement();
        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<HpdAspNetCorePackageRuntime>();

        var response = runtime.EnableRegistered(packageId);

        response.Package.Id.Should().Be(packageId);
        response.Package.Contributions.AgentContributors.Should().Contain("test.agent");
    }

    private sealed class TestPackage : HpdPackage
    {
        public TestPackage(string id)
        {
            Manifest = new HpdPackageManifest(id, "Test Package", new Version(1, 0));
        }

        public override HpdPackageManifest Manifest { get; }

        public override void Configure(IHpdPackageBuilder builder)
        {
            builder.AddAgentContributor("test.agent", new DelegateAgentBuilderContributor(_ => { }));
        }
    }
}
