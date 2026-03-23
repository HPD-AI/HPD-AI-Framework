using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.Mistral;

/// <summary>
/// Auto-discovers and registers the Mistral provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class MistralProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderDiscovery.RegisterProviderFactory(() => new MistralProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        ProviderDiscovery.RegisterProviderConfigType<MistralProviderConfig>(
            "mistral",
            json => JsonSerializer.Deserialize(json, MistralJsonContext.Default.MistralProviderConfig),
            config => JsonSerializer.Serialize(config, MistralJsonContext.Default.MistralProviderConfig));

        // Register environment variable aliases for secret resolution
        SecretAliasRegistry.Register("mistral:ApiKey", "MISTRAL_API_KEY");
    }
}
