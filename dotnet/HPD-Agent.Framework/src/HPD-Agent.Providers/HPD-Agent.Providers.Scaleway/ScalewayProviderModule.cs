using HPD.Agent.Providers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.Scaleway;

public static class ScalewayProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new ScalewayProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<ScalewayProviderConfig>(
            "scaleway",
            json => JsonSerializer.Deserialize(json, ScalewayJsonContext.Default.ScalewayProviderConfig),
            config => JsonSerializer.Serialize(config, ScalewayJsonContext.Default.ScalewayProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("scaleway:ApiKey", "SCW_SECRET_KEY", "SCALEWAY_API_KEY", "SCW_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("scaleway:Endpoint", "SCALEWAY_ENDPOINT", "SCALEWAY_BASE_URL", "SCW_ENDPOINT", "SCW_BASE_URL");
    }
}
