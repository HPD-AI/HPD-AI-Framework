using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.Packages;
using HPD.Agent.Packages.DynamicDotNet;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Packages;

public sealed class HpdDynamicDotNetPackageTests
{
    [Fact]
    public void Loader_CreatesPackageFromDotNetEntrypoint()
    {
        var loader = new HpdDotNetPackageLoader();
        var manifest = CreateManifest();

        var loaded = loader.Load(manifest, typeof(RuntimePackageForDynamicDotNet).Assembly);

        loaded.Package.Should().BeOfType<RuntimePackageForDynamicDotNet>();
        Assert.Same(typeof(RuntimePackageForDynamicDotNet).Assembly, loaded.Assembly);
    }

    [Fact]
    public void Loader_WhenPackageIdentityDoesNotMatchManifest_Throws()
    {
        var loader = new HpdDotNetPackageLoader();
        var manifest = CreateManifest() with
        {
            Id = "hpd.test.different"
        };

        var act = () => loader.Load(manifest, typeof(RuntimePackageForDynamicDotNet).Assembly);

        act.Should()
            .Throw<HpdDotNetPackageLoadException>()
            .WithMessage("*does not match manifest id*");
    }

    [Fact]
    public void EnableFromDotNetManifest_LoadsPackageAndAppliesContributions()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));

        var loaded = manager.EnableFromDotNetManifest(
            CreateManifest(),
            HpdPackageScopes.User);

        loaded.State.Should().Be(HpdPackageLoadState.Enabled);
        loaded.Owner.Should().Be(new HpdContributionOwner(
            "hpd.test.dynamic-dotnet",
            HpdPackageScopes.User,
            "3.0.0",
            "Dynamic DotNet Package"));
        agentContributors.Contributions.Should().Contain(contribution =>
            contribution.Key == "hpd.test.dynamic-dotnet.generated-agent-catalog" &&
            contribution.Owner == loaded.Owner);
        agentContributors.Contributions.Should().Contain(contribution =>
            contribution.Key == "hpd.test.dynamic-dotnet.agent" &&
            contribution.Owner == loaded.Owner);
    }

    [Fact]
    public void EnableFromDotNetManifest_WhenCandidateCannotLoad_KeepsPreviousPackageActive()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        var previous = manager.EnableFromDotNetManifest(CreateManifest());
        var badManifest = CreateManifest() with
        {
            Entrypoints = new HpdPackageEntrypoints
            {
                DotNet = new HpdDotNetPackageEntrypoint
                {
                    Assembly = typeof(RuntimePackageForDynamicDotNet).Assembly.Location,
                    PackageType = "HPD.Agent.Tests.Packages.MissingRuntimePackage"
                }
            }
        };

        var failed = manager.EnableFromDotNetManifest(badManifest);

        failed.State.Should().Be(HpdPackageLoadState.Failed);
        failed.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Message.Contains("Package load failed", StringComparison.Ordinal));
        manager.Packages.Should().ContainSingle(package =>
            package.Id == previous.Id &&
            package.State == HpdPackageLoadState.Enabled &&
            package.Owner == previous.Owner);
        agentContributors.Contributions.Should().Contain(contribution =>
            contribution.Key == "hpd.test.dynamic-dotnet.agent" &&
            contribution.Owner == previous.Owner);
    }

    private static HpdPackageManifest CreateManifest() => new(
        "hpd.test.dynamic-dotnet",
        "Dynamic DotNet Package",
        new Version(3, 0, 0))
    {
        Trust = HpdPackageTrust.Trusted,
        LoadMode = HpdPackageLoadMode.RuntimeInProcessDotNet,
        Targets = new HpdPackageTargets
        {
            Backend = new HpdPackageTarget
            {
                Required = true,
                Entrypoint = HpdPackageEntrypointKind.DotNet
            }
        },
        Entrypoints = new HpdPackageEntrypoints
        {
            DotNet = new HpdDotNetPackageEntrypoint
            {
                Assembly = typeof(RuntimePackageForDynamicDotNet).Assembly.Location,
                PackageType = typeof(RuntimePackageForDynamicDotNet).FullName!
            }
        },
        Contributes = new HpdPackageContributes
        {
            Agent = true
        }
    };
}

public sealed class RuntimePackageForDynamicDotNet : HpdPackage
{
    public override HpdPackageManifest Manifest { get; } = new(
        "hpd.test.dynamic-dotnet",
        "Dynamic DotNet Package",
        new Version(3, 0, 0))
    {
        Trust = HpdPackageTrust.Trusted,
        LoadMode = HpdPackageLoadMode.RuntimeInProcessDotNet,
        Contributes = new HpdPackageContributes
        {
            Agent = true
        }
    };

    public override void Configure(IHpdPackageBuilder builder)
    {
        builder.AddAgentContributor(
            "hpd.test.dynamic-dotnet.agent",
            new DelegateAgentBuilderContributor(_ => { }));
    }
}
