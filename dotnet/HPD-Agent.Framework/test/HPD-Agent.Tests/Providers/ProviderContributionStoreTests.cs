using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Providers;

public sealed class ProviderContributionStoreTests
{
    [Fact]
    public void AddProviderFactory_RecordsOwnerAndBuildsRegistry()
    {
        var owner = new HpdContributionOwner("hpd.test.provider", "test");
        var store = new ProviderContributionStore();

        store.AddProviderFactory(
            "test",
            _ => new TestProvider("test"),
            owner);

        store.ProviderFactories.Should().ContainSingle(contribution =>
            contribution.Key == "test" &&
            contribution.Owner == owner);
        var registry = store.BuildRegistry();
        registry.IsRegistered("test").Should().BeTrue();
    }

    [Fact]
    public void RemoveOwner_RemovesOwnedProviderFactoriesAndConfigSerializers()
    {
        var owner = new HpdContributionOwner("hpd.test.provider", "test");
        var store = new ProviderContributionStore();
        store.AddProviderFactory("test", _ => new TestProvider("test"), owner);
        store.AddProviderConfigSerializer(
            "test",
            ProviderClientFamily.Chat,
            new ProviderConfigRegistration(
                typeof(TestProviderConfig),
                json => new TestProviderConfig(json),
                config => ((TestProviderConfig)config).Value),
            owner);

        store.RemoveOwner(owner).Should().BeTrue();

        store.ProviderFactories.Should().BeEmpty();
        store.GetProviderConfigSerializer("test", ProviderClientFamily.Chat).Should().BeNull();
    }

    [Fact]
    public void AddSecretAlias_RecordsOwnerAndAppliesEffectiveAlias()
    {
        var owner = new HpdContributionOwner("hpd.test.provider", "test");
        var store = new ProviderContributionStore();

        store.AddSecretAlias(
            "test-store:ApiKey",
            ["TEST_API_KEY", "TEST_API_TOKEN"],
            owner);

        string[] expectedAliases = ["TEST_API_KEY", "TEST_API_TOKEN"];
        store.SecretAliases.Should().ContainSingle(contribution =>
            contribution.Key == "test-store:ApiKey" &&
            contribution.Owner == owner &&
            contribution.Value.EnvironmentVariableNames.SequenceEqual(expectedAliases));
        SecretAliasRegistry.GetAll()["test-store:ApiKey"].Should().Equal("TEST_API_KEY", "TEST_API_TOKEN");
    }

    [Fact]
    public void RemoveOwner_RemovesOwnedSecretAliasesFromStoreAndEffectiveRegistry()
    {
        var owner = new HpdContributionOwner("hpd.test.provider", "test");
        var store = new ProviderContributionStore();
        store.AddSecretAlias("test-remove:ApiKey", ["TEST_API_KEY"], owner);

        store.RemoveOwner(owner).Should().BeTrue();

        store.SecretAliases.Should().BeEmpty();
        SecretAliasRegistry.GetAll().Should().NotContainKey("test-remove:ApiKey");
    }

    [Fact]
    public async Task AddModelCatalog_RecordsOwnerAndReturnsCatalog()
    {
        var owner = new HpdContributionOwner("hpd.test.provider", "test");
        var store = new ProviderContributionStore();
        var catalog = new TestProviderModelCatalog();

        store.AddModelCatalog(catalog, owner);

        store.ModelCatalogs.Should().ContainSingle(contribution =>
            contribution.Key == "test" &&
            contribution.Owner == owner &&
            ReferenceEquals(contribution.Value, catalog));
        store.GetModelCatalog("test").Should().BeSameAs(catalog);

        var models = await catalog.GetModelsAsync(
            new ProviderModelCatalogContext(new ServiceCollection().BuildServiceProvider()),
            new ProviderModelQuery());
        models.Should().ContainSingle(model =>
            model.ProviderKey == "test" &&
            model.ModelId == "test-model" &&
            model.SupportsTools);
    }

    [Fact]
    public void RemoveOwner_RemovesModelCatalogs()
    {
        var owner = new HpdContributionOwner("hpd.test.provider", "test");
        var store = new ProviderContributionStore();
        store.AddModelCatalog(new TestProviderModelCatalog(), owner);

        store.RemoveOwner(owner).Should().BeTrue();

        store.ModelCatalogs.Should().BeEmpty();
        store.GetModelCatalog("test").Should().BeNull();
    }

    [Fact]
    public void AgentBuilder_WithProviderRegistry_ReplacesEffectiveRegistry()
    {
        var registry = new ProviderRegistry();
        registry.Register(new TestProvider("custom"));

        var builder = new AgentBuilder()
            .WithProviderRegistry(registry);

        builder.ProviderRegistry.Should().BeSameAs(registry);
        builder.IsProviderRegistered("custom").Should().BeTrue();
    }

    private sealed record TestProviderConfig(string Value);

    private sealed class TestProviderModelCatalog : IProviderModelCatalog
    {
        public string ProviderKey => "test";

        public ValueTask<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(
            ProviderModelCatalogContext context,
            ProviderModelQuery query,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ProviderModelDescriptor>>(
                [new ProviderModelDescriptor("test", "test-model", SupportsTools: true)]);
    }

    private sealed class TestProvider : IProvider
    {
        public TestProvider(string providerKey)
        {
            ProviderKey = providerKey;
        }

        public string ProviderKey { get; }

        public string DisplayName => ProviderKey;

        public IProviderErrorHandler CreateErrorHandler() => new TestProviderErrorHandler();

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

    private sealed class TestProviderErrorHandler : IProviderErrorHandler
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
