using FluentAssertions;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.Configuration;

namespace HPD.Agent.ModelsDev.Tests;

public sealed class HpdModelsDevProviderStateTests
{
    [Fact]
    public async Task GetStatusAsync_authenticates_from_provider_section_api_key()
    {
        var registry = new ProviderRegistry();
        registry.Register(new FakeProvider("openrouter"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:openrouter:ApiKey"] = "test-key"
            })
            .Build();
        var state = new HpdModelsDevProviderState(
            registry,
            new ConfigurationSecretResolver(configuration));

        var status = await state.GetStatusAsync("openrouter");

        status.IsRegistered.Should().BeTrue();
        status.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_does_not_authenticate_from_capitalized_provider_section()
    {
        var registry = new ProviderRegistry();
        registry.Register(new FakeProvider("openrouter"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:Openrouter:ApiKey"] = "test-key"
            })
            .Build();
        var state = new HpdModelsDevProviderState(
            registry,
            new ConfigurationSecretResolver(configuration));

        var status = await state.GetStatusAsync("openrouter");

        status.IsRegistered.Should().BeTrue();
        status.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_authenticates_multi_key_provider_from_provider_section()
    {
        var registry = new ProviderRegistry();
        registry.Register(new FakeProvider("azure-ai"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:azure-ai:ApiKey"] = "test-key",
                ["Providers:azure-ai:Endpoint"] = "https://example.test"
            })
            .Build();
        var state = new HpdModelsDevProviderState(
            registry,
            new ConfigurationSecretResolver(configuration));

        var status = await state.GetStatusAsync("azure-ai");

        status.IsRegistered.Should().BeTrue();
        status.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_requires_all_multi_key_provider_values()
    {
        var registry = new ProviderRegistry();
        registry.Register(new FakeProvider("azure-ai"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:azure-ai:ApiKey"] = "test-key"
            })
            .Build();
        var state = new HpdModelsDevProviderState(
            registry,
            new ConfigurationSecretResolver(configuration));

        var status = await state.GetStatusAsync("azure-ai");

        status.IsRegistered.Should().BeTrue();
        status.IsAuthenticated.Should().BeFalse();
    }

    private sealed class FakeProvider(string providerKey) : IProvider
    {
        public string ProviderKey { get; } = providerKey;

        public string DisplayName => ProviderKey;

        public IProviderErrorHandler CreateErrorHandler()
            => new GenericErrorHandler();

        public ProviderMetadata GetMetadata()
            => new()
            {
                ProviderKey = ProviderKey,
                DisplayName = DisplayName
            };

        public ProviderValidationResult ValidateConfiguration(
            ClientProviderConfig config,
            ProviderClientFamily family)
            => ProviderValidationResult.Success();
    }
}
