using HPD.Agent.Providers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.OVHcloud;

public static class OVHcloudProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new OVHcloudProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<OVHcloudProviderConfig>(
            "ovhcloud",
            json => JsonSerializer.Deserialize(json, OVHcloudJsonContext.Default.OVHcloudProviderConfig),
            config => JsonSerializer.Serialize(config, OVHcloudJsonContext.Default.OVHcloudProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("ovhcloud:ApiKey", "OVHCLOUD_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("ovhcloud:Endpoint", "OVHCLOUD_ENDPOINT", "OVHCLOUD_BASE_URL");
    }
}
