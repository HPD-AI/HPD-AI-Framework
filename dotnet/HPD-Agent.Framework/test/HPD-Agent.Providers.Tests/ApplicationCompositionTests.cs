using HPD.Agent;
using HPD.Agent.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Tests;

public sealed class ApplicationCompositionTests
{
    [Fact]
    public void AddOpenAI_ContributesProviderWithoutAssemblyLoadWorkaround()
    {
        var services = new ServiceCollection();
        services.AddHPD(hpd => hpd.AddOpenAI());

        using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<HPDApplicationComposition>();

        Assert.Contains(
            composition.Providers.Descriptors.Providers,
            descriptor => descriptor.ProviderKey == "openai");
    }

    [Fact]
    public void AddHPD_ComposesMultipleProviderPackages()
    {
        var services = new ServiceCollection();
        services.AddHPD(hpd => hpd
            .AddOpenAI()
            .AddAnthropic());

        using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<HPDApplicationComposition>();

        Assert.Contains(
            composition.Providers.Descriptors.Providers,
            descriptor => descriptor.ProviderKey == "openai");
        Assert.Contains(
            composition.Providers.Descriptors.Providers,
            descriptor => descriptor.ProviderKey == "anthropic");
    }

    [Fact]
    public void OpenAIPackage_ExposesDistinctProviderExtensions()
    {
        var services = new ServiceCollection();
        services.AddHPD(hpd => hpd
            .AddOpenAI()
            .AddAzureOpenAI());

        using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<HPDApplicationComposition>();

        Assert.Contains(
            composition.Providers.Descriptors.Providers,
            descriptor => descriptor.ProviderKey == "openai");
        Assert.Contains(
            composition.Providers.Descriptors.Providers,
            descriptor => descriptor.ProviderKey == "azure-openai");
    }
}
