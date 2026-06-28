using HPD.Agent.Providers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.Zai;

public static class ZaiProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new ZaiProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<ZaiProviderConfig>(
            "zai",
            json => JsonSerializer.Deserialize(json, ZaiJsonContext.Default.ZaiProviderConfig),
            config => JsonSerializer.Serialize(config, ZaiJsonContext.Default.ZaiProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("zai:ApiKey", "ZAI_API_KEY", "Z_AI_API_KEY", "BIGMODEL_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("zai:Endpoint", "ZAI_ENDPOINT", "ZAI_BASE_URL", "Z_AI_ENDPOINT", "Z_AI_BASE_URL", "BIGMODEL_ENDPOINT", "BIGMODEL_BASE_URL");
    }
}
