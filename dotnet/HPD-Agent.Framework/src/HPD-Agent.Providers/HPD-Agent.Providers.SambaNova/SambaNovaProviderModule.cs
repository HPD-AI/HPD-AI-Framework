using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.SambaNova;

public static class SambaNovaProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new SambaNovaProvider());

        ProviderDiscovery.RegisterProviderConfigType<SambaNovaProviderConfig>(
            "sambanova",
            json => JsonSerializer.Deserialize(json, SambaNovaJsonContext.Default.SambaNovaProviderConfig),
            config => JsonSerializer.Serialize(config, SambaNovaJsonContext.Default.SambaNovaProviderConfig));

        SecretAliasRegistry.Register("sambanova:ApiKey", "SAMBANOVA_API_KEY");
        SecretAliasRegistry.Register("sambanova:Endpoint", "SAMBANOVA_ENDPOINT", "SAMBANOVA_BASE_URL");
    }
}
