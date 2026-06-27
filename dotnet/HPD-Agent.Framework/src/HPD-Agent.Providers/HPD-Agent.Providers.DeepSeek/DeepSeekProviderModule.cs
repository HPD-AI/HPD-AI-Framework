using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.DeepSeek;

public static class DeepSeekProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new DeepSeekProvider());

        ProviderDiscovery.RegisterProviderConfigType<DeepSeekProviderConfig>(
            "deepseek",
            json => JsonSerializer.Deserialize(json, DeepSeekJsonContext.Default.DeepSeekProviderConfig),
            config => JsonSerializer.Serialize(config, DeepSeekJsonContext.Default.DeepSeekProviderConfig));

        SecretAliasRegistry.Register("deepseek:ApiKey", "DEEPSEEK_API_KEY");
        SecretAliasRegistry.Register("deepseek:Endpoint", "DEEPSEEK_ENDPOINT", "DEEPSEEK_BASE_URL");
    }
}
