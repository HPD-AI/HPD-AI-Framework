using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.DashScope;

/// <summary>
/// Auto-discovers and registers the DashScope provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class DashScopeProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        ProviderContributionRegistry.RegisterProviderFactory(() => new DashScopeProvider());

        ProviderContributionRegistry.RegisterProviderConfigType<DashScopeProviderConfig>(
            "dashscope",
            json => JsonSerializer.Deserialize(json, DashScopeJsonContext.Default.DashScopeProviderConfig),
            config => JsonSerializer.Serialize(config, DashScopeJsonContext.Default.DashScopeProviderConfig));
        ProviderContributionRegistry.RegisterProviderConfigType<DashScopeProviderConfig>(
            "dashscope",
            ProviderClientFamily.Embeddings,
            json => JsonSerializer.Deserialize(json, DashScopeJsonContext.Default.DashScopeProviderConfig),
            config => JsonSerializer.Serialize(config, DashScopeJsonContext.Default.DashScopeProviderConfig));

        ProviderContributionRegistry.RegisterSecretAlias("dashscope:ApiKey", "DASHSCOPE_API_KEY", "QWEN_API_KEY", "DASHSCOPE_KEY");
    }
}
