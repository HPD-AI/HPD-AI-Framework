using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests.Core;

public sealed class GeneratedProviderCompositionIntegrationTests
{
    [Fact]
    public void GeneratedComposition_RegistersAnthropicWithoutModuleInitializer()
    {
        var services = new ServiceCollection();
        services.AddHpdGeneratedProviders();
        using var provider = services.BuildServiceProvider();

        var composition = provider.GetRequiredService<ProviderComposition>();
        Assert.True(composition.Descriptors.TryGet("anthropic", out var descriptor));
        var registration = composition.Runtime.GetFactory("anthropic", ProviderClientFamily.Chat);

        Assert.Equal("Anthropic (Claude)", descriptor!.DisplayName);
        Assert.Equal(["ANTHROPIC_API_KEY"],
            composition.SecretAliases.GetEnvironmentVariables("anthropic:ApiKey"));
        Assert.Equal("anthropic", registration.Factory().ProviderKey);
    }

    [Fact]
    public void AgentBuilder_CompositionConstructor_MaterializesGeneratedProviders()
    {
        var services = new ServiceCollection();
        services.AddHpdGeneratedProviders();
        using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<ProviderComposition>();

        var builder = new AgentBuilder(new AgentConfig(), composition);

        Assert.True(builder.ProviderRegistry.IsRegistered("anthropic"));
    }
}
