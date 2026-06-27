using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.Cerebras;

public static class CerebrasProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new CerebrasProvider());

        ProviderDiscovery.RegisterProviderConfigType<CerebrasProviderConfig>(
            "cerebras",
            json => JsonSerializer.Deserialize(json, CerebrasJsonContext.Default.CerebrasProviderConfig),
            config => JsonSerializer.Serialize(config, CerebrasJsonContext.Default.CerebrasProviderConfig));

        SecretAliasRegistry.Register("cerebras:ApiKey", "CEREBRAS_API_KEY");
        SecretAliasRegistry.Register("cerebras:Endpoint", "CEREBRAS_ENDPOINT", "CEREBRAS_BASE_URL");
    }
}
