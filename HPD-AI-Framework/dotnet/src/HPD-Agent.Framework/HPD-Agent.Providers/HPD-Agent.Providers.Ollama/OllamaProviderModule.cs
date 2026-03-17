using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;

namespace HPD.Agent.Providers.Ollama;

/// <summary>
/// Auto-discovers and registers the Ollama provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class OllamaProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderDiscovery.RegisterProviderFactory(() => new OllamaProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        ProviderDiscovery.RegisterProviderConfigType<OllamaProviderConfig>(
            "ollama",
            json => JsonSerializer.Deserialize(json, OllamaJsonContext.Default.OllamaProviderConfig),
            config => JsonSerializer.Serialize(config, OllamaJsonContext.Default.OllamaProviderConfig));

        // No API key registration needed - Ollama runs locally and does not require authentication
    }
}
