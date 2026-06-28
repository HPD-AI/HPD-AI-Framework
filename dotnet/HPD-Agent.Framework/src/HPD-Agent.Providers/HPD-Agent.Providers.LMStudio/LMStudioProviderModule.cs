using HPD.Agent.Providers;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace HPD.Agent.Providers.LMStudio;

public static class LMStudioProviderModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new LMStudioProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<LMStudioProviderConfig>(
            "lmstudio",
            json => JsonSerializer.Deserialize(json, LMStudioJsonContext.Default.LMStudioProviderConfig),
            config => JsonSerializer.Serialize(config, LMStudioJsonContext.Default.LMStudioProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("lmstudio:ApiKey", "LMSTUDIO_API_KEY", "LM_STUDIO_API_KEY");
        ProviderContributionRegistry.RegisterSecretAlias(
            "lmstudio:Endpoint",
            "LMSTUDIO_ENDPOINT",
            "LMSTUDIO_BASE_URL",
            "LMSTUDIO_API_BASE",
            "LM_STUDIO_ENDPOINT",
            "LM_STUDIO_BASE_URL",
            "LM_STUDIO_API_BASE");
    }
}
