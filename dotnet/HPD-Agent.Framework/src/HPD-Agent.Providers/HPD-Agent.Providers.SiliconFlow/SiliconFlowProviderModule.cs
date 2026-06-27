using HPD.Agent.Providers;
using HPD.Agent.Secrets;
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
        ProviderDiscovery.RegisterProviderFactory(() => new SiliconFlowProvider());

        ProviderDiscovery.RegisterProviderConfigType<SiliconFlowProviderConfig>(
            "siliconflow",
            json => JsonSerializer.Deserialize(json, SiliconFlowJsonContext.Default.SiliconFlowProviderConfig),
            config => JsonSerializer.Serialize(config, SiliconFlowJsonContext.Default.SiliconFlowProviderConfig));

        SecretAliasRegistry.Register("siliconflow:ApiKey", "SILICONFLOW_API_KEY");
        SecretAliasRegistry.Register("siliconflow:Endpoint", "SILICONFLOW_ENDPOINT", "SILICONFLOW_BASE_URL");
    }
}
