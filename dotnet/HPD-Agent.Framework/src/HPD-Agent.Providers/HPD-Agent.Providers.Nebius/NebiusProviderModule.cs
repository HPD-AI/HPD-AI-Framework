using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.Nebius;

public static class NebiusProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new NebiusProvider());

        ProviderDiscovery.RegisterProviderConfigType<NebiusProviderConfig>(
            "nebius",
            json => JsonSerializer.Deserialize(json, NebiusJsonContext.Default.NebiusProviderConfig),
            config => JsonSerializer.Serialize(config, NebiusJsonContext.Default.NebiusProviderConfig));

        SecretAliasRegistry.Register("nebius:ApiKey", "NEBIUS_API_KEY");
        SecretAliasRegistry.Register("nebius:Endpoint", "NEBIUS_ENDPOINT", "NEBIUS_BASE_URL");
    }
}
