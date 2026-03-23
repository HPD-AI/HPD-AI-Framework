using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// Auto-discovers and registers the HuggingFace provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class HuggingFaceProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderDiscovery.RegisterProviderFactory(() => new HuggingFaceProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        ProviderDiscovery.RegisterProviderConfigType<HuggingFaceProviderConfig>(
            "huggingface",
            json => JsonSerializer.Deserialize(json, HuggingFaceJsonContext.Default.HuggingFaceProviderConfig),
            config => JsonSerializer.Serialize(config, HuggingFaceJsonContext.Default.HuggingFaceProviderConfig));

        // Register environment variable aliases for secret resolution
        SecretAliasRegistry.Register("huggingface:ApiKey", "HUGGINGFACE_API_KEY", "HF_TOKEN");
    }
}
