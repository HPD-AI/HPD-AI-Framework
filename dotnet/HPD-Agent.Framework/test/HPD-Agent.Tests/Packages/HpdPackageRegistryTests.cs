using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Packages;
using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Packages;

public sealed class HpdPackageRegistryTests : IDisposable
{
    public HpdPackageRegistryTests()
    {
        HpdPackageRegistry.ClearForTesting();
    }

    public void Dispose()
    {
        HpdPackageRegistry.ClearForTesting();
    }

    [Fact]
    public void Register_ReplacesPackageWithSameId()
    {
        HpdPackageRegistry.Register(new TestPackage("hpd.test.package", new Version(1, 0, 0)));
        HpdPackageRegistry.Register(new TestPackage("hpd.test.package", new Version(2, 0, 0)));

        HpdPackageRegistry.Snapshot().Should().ContainSingle(package =>
            package.Id == "hpd.test.package" &&
            package.Version == new Version(2, 0, 0));
    }

    [Fact]
    public void Register_GenericCreatesAndRegistersPackage()
    {
        HpdPackageRegistry.Register<GenericRegisteredPackage>();

        HpdPackageRegistry.Snapshot().Should().ContainSingle(package =>
            package.Id == "hpd.test.generic-registered" &&
            package.Version == new Version(1, 0, 0));
    }

    [Fact]
    public void Snapshot_ReturnsPackagesInDeterministicIdOrder()
    {
        HpdPackageRegistry.Register(new TestPackage("hpd.z"));
        HpdPackageRegistry.Register(new TestPackage("hpd.a"));

        HpdPackageRegistry.Snapshot()
            .Select(package => package.Id)
            .Should()
            .Equal("hpd.a", "hpd.z");
    }

    [Fact]
    public void EnableRegisteredPackages_AppliesRegisteredPackages()
    {
        var agentContributors = new AgentBuilderContributorStore();
        var providerContributions = new ProviderContributionStore();
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(agentContributors, providerContributions));
        HpdPackageRegistry.Register(new TestPackage("hpd.a"));
        HpdPackageRegistry.Register(new TestPackage("hpd.b"));

        var loaded = manager.EnableRegisteredPackages(HpdPackageScopes.App);

        loaded.Should().HaveCount(2);
        loaded.Should().OnlyContain(package => package.State == HpdPackageLoadState.Enabled);
        manager.Packages.Select(package => package.Id).Should().Equal("hpd.a", "hpd.b");
        providerContributions.ProviderFactories.Should().HaveCount(2);
    }

    [Fact]
    public void EnableRegistered_ThrowsForUnknownPackage()
    {
        var manager = new HpdPackageManager(
            new ServiceCollection(),
            new HpdPackageContributionStores(
                new AgentBuilderContributorStore(),
                new ProviderContributionStore()));

        var act = () => manager.EnableRegistered("hpd.missing");

        act.Should().Throw<KeyNotFoundException>()
            .WithMessage("*hpd.missing*");
    }

    private sealed class TestPackage : HpdPackage
    {
        public TestPackage(
            string id,
            Version? version = null)
        {
            Manifest = new HpdPackageManifest(id, id, version ?? new Version(1, 0, 0))
            {
                Trust = HpdPackageTrust.Trusted,
                LoadMode = HpdPackageLoadMode.BuildTimeInProcess,
                Contributes = new HpdPackageContributes
                {
                    Providers = true
                }
            };
        }

        public override HpdPackageManifest Manifest { get; }

        public override void Configure(IHpdPackageBuilder builder)
        {
            builder.AddProviderContributor(new DelegateProviderContributor(
                providerBuilder => providerBuilder.AddProviderFactory(Id, _ => new RegistryTestProvider(Id))));
        }
    }

    private sealed class GenericRegisteredPackage : HpdPackage
    {
        public override HpdPackageManifest Manifest { get; } = new(
            "hpd.test.generic-registered",
            "Generic Registered",
            new Version(1, 0, 0))
        {
            Trust = HpdPackageTrust.Trusted,
            LoadMode = HpdPackageLoadMode.BuildTimeInProcess
        };

        public override void Configure(IHpdPackageBuilder builder)
        {
        }
    }

    private sealed class DelegateProviderContributor : IProviderContributor
    {
        private readonly Action<IProviderContributionBuilder> _configure;

        public DelegateProviderContributor(Action<IProviderContributionBuilder> configure)
        {
            _configure = configure;
        }

        public void ConfigureProviders(
            IProviderContributionBuilder builder,
            HpdProviderContributionContext context)
            => _configure(builder);
    }

    private sealed class RegistryTestProvider : IProvider
    {
        public RegistryTestProvider(string providerKey)
        {
            ProviderKey = providerKey;
        }

        public string ProviderKey { get; }

        public string DisplayName => ProviderKey;

        public IProviderErrorHandler CreateErrorHandler() =>
            new RegistryTestProviderErrorHandler();

        public ProviderMetadata GetMetadata() => new()
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName
        };

        public ProviderValidationResult ValidateConfiguration(
            ClientProviderConfig config,
            ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }

    private sealed class RegistryTestProviderErrorHandler : IProviderErrorHandler
    {
        public ProviderErrorDetails? ParseError(Exception exception) => new()
        {
            Message = exception.Message
        };

        public TimeSpan? GetRetryDelay(
            ProviderErrorDetails details,
            int attempt,
            TimeSpan initialDelay,
            double multiplier,
            TimeSpan maxDelay)
            => null;

        public bool RequiresSpecialHandling(ProviderErrorDetails details) => false;
    }
}
