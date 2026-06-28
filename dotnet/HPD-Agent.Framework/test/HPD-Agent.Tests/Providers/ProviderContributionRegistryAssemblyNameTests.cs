using System.Reflection;
using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Providers;

public sealed class ProviderContributionRegistryAssemblyNameTests
{
    [Theory]
    [InlineData("HPD-Agent.Providers.OpenAI", true)]
    [InlineData("HPD-Agent.Providers.Mistral", true)]
    [InlineData("HPD.Agent.Providers.Audio.OpenAI", true)]
    [InlineData("HPD.Agent.Providers.Audio.ElevenLabs", true)]
    [InlineData("HPD-Agent.AudioProviders.OpenAI", false)]
    [InlineData("HPD-Agent.AudioProviders.ElevenLabs", false)]
    [InlineData("HPD-Agent.Audio", false)]
    public void IsProviderAssemblyName_UsesCurrentProviderAssemblyFamilies(
        string assemblyName,
        bool expected)
    {
        var method = typeof(ProviderContributionRegistry).GetMethod(
            "IsProviderAssemblyName",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var actual = Assert.IsType<bool>(method.Invoke(null, [assemblyName]));

        Assert.Equal(expected, actual);
    }
}
