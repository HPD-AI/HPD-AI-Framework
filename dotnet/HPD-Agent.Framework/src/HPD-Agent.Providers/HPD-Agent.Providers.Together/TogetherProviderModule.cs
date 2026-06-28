using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Together;

/// <summary>
/// Auto-discovers and registers the Together AI provider on assembly load.
/// </summary>
public static class TogetherProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new TogetherProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<TogetherProviderConfig>(
            "together",
            json => JsonSerializer.Deserialize(json, TogetherJsonContext.Default.TogetherProviderConfig),
            config => JsonSerializer.Serialize(config, TogetherJsonContext.Default.TogetherProviderConfig));
        ProviderContributionRegistry.RegisterProviderConfigType<TogetherProviderConfig>(
            "together",
            ProviderClientFamily.Embeddings,
            json => JsonSerializer.Deserialize(json, TogetherJsonContext.Default.TogetherProviderConfig),
            config => JsonSerializer.Serialize(config, TogetherJsonContext.Default.TogetherProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("together:ApiKey", "TOGETHER_API_KEY");
    }
}
