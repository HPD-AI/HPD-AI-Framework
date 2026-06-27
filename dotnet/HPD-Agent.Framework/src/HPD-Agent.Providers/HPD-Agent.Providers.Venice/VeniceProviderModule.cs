using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.Venice;

public static class VeniceProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new VeniceProvider());

        ProviderDiscovery.RegisterProviderConfigType<VeniceProviderConfig>(
            "venice",
            json => JsonSerializer.Deserialize(json, VeniceJsonContext.Default.VeniceProviderConfig),
            config => JsonSerializer.Serialize(config, VeniceJsonContext.Default.VeniceProviderConfig));

        SecretAliasRegistry.Register("venice:ApiKey", "VENICE_API_KEY");
        SecretAliasRegistry.Register("venice:Endpoint", "VENICE_ENDPOINT", "VENICE_BASE_URL");
    }
}
