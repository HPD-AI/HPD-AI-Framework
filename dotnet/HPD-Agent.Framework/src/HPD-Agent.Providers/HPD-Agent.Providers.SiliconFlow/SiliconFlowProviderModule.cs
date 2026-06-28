using HPD.Agent.Providers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.SiliconFlow;

public static class SiliconFlowProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new SiliconFlowProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<SiliconFlowProviderConfig>(
            "siliconflow",
            json => JsonSerializer.Deserialize(json, SiliconFlowJsonContext.Default.SiliconFlowProviderConfig),
            config => JsonSerializer.Serialize(config, SiliconFlowJsonContext.Default.SiliconFlowProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("siliconflow:ApiKey", "SILICONFLOW_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias("siliconflow:Endpoint", "SILICONFLOW_ENDPOINT", "SILICONFLOW_BASE_URL");
    }
}
