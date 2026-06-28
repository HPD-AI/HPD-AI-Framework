using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Moonshot;

/// <summary>
/// Auto-discovers and registers the Moonshot provider on assembly load.
/// </summary>
public static class MoonshotProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new MoonshotProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<MoonshotProviderConfig>(
            "moonshot",
            json => JsonSerializer.Deserialize(json, MoonshotJsonContext.Default.MoonshotProviderConfig),
            config => JsonSerializer.Serialize(config, MoonshotJsonContext.Default.MoonshotProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("moonshot:ApiKey", "MOONSHOT_API_KEY", "KIMI_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("moonshot:Endpoint", "MOONSHOT_ENDPOINT", "MOONSHOT_BASE_URL", "KIMI_ENDPOINT", "KIMI_BASE_URL");
    }
}
