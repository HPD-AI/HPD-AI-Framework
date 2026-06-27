using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.NvidiaNim;

public static class NvidiaNimProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderDiscovery.RegisterProviderFactory(() => new NvidiaNimProvider());

        ProviderDiscovery.RegisterProviderConfigType<NvidiaNimProviderConfig>(
            "nvidia-nim",
            json => JsonSerializer.Deserialize(json, NvidiaNimJsonContext.Default.NvidiaNimProviderConfig),
            config => JsonSerializer.Serialize(config, NvidiaNimJsonContext.Default.NvidiaNimProviderConfig));

        SecretAliasRegistry.Register("nvidia-nim:ApiKey", "NVIDIA_API_KEY", "NVIDIA_NIM_API_KEY");
        SecretAliasRegistry.Register("nvidia-nim:Endpoint", "NVIDIA_NIM_ENDPOINT", "NVIDIA_NIM_BASE_URL", "NVIDIA_ENDPOINT", "NVIDIA_BASE_URL");
    }
}
