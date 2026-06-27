using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.Nscale;

public static class NscaleProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new NscaleProvider());

        ProviderDiscovery.RegisterProviderConfigType<NscaleProviderConfig>(
            "nscale",
            json => JsonSerializer.Deserialize(json, NscaleJsonContext.Default.NscaleProviderConfig),
            config => JsonSerializer.Serialize(config, NscaleJsonContext.Default.NscaleProviderConfig));

        SecretAliasRegistry.Register("nscale:ApiKey", "NSCALE_API_KEY");
        SecretAliasRegistry.Register("nscale:Endpoint", "NSCALE_ENDPOINT", "NSCALE_BASE_URL");
    }
}
