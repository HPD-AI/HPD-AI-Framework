using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.DeepInfra;

/// <summary>
/// Auto-discovers and registers the DeepInfra provider on assembly load.
/// </summary>
public static class DeepInfraProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new DeepInfraProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<DeepInfraProviderConfig>(
            "deepinfra",
            json => JsonSerializer.Deserialize(json, DeepInfraJsonContext.Default.DeepInfraProviderConfig),
            config => JsonSerializer.Serialize(config, DeepInfraJsonContext.Default.DeepInfraProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("deepinfra:ApiKey", "DEEPINFRA_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("deepinfra:Endpoint", "DEEPINFRA_ENDPOINT", "DEEPINFRA_BASE_URL");
    }
}
