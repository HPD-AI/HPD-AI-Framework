using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.Fireworks;

/// <summary>
/// Auto-discovers and registers the Fireworks AI provider on assembly load.
/// </summary>
public static class FireworksProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new FireworksProvider());

        ProviderDiscovery.RegisterProviderConfigType<FireworksProviderConfig>(
            "fireworks",
            json => JsonSerializer.Deserialize(json, FireworksJsonContext.Default.FireworksProviderConfig),
            config => JsonSerializer.Serialize(config, FireworksJsonContext.Default.FireworksProviderConfig));

        SecretAliasRegistry.Register("fireworks:ApiKey", "FIREWORKS_API_KEY");
        SecretAliasRegistry.Register("fireworks:Endpoint", "FIREWORKS_ENDPOINT", "FIREWORKS_BASE_URL");
    }
}
