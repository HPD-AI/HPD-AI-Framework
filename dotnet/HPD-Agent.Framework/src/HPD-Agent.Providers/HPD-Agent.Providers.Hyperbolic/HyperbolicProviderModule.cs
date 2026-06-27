using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.Hyperbolic;

public static class HyperbolicProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new HyperbolicProvider());

        ProviderDiscovery.RegisterProviderConfigType<HyperbolicProviderConfig>(
            "hyperbolic",
            json => JsonSerializer.Deserialize(json, HyperbolicJsonContext.Default.HyperbolicProviderConfig),
            config => JsonSerializer.Serialize(config, HyperbolicJsonContext.Default.HyperbolicProviderConfig));

        SecretAliasRegistry.Register("hyperbolic:ApiKey", "HYPERBOLIC_API_KEY");
        SecretAliasRegistry.Register("hyperbolic:Endpoint", "HYPERBOLIC_ENDPOINT", "HYPERBOLIC_BASE_URL");
    }
}
