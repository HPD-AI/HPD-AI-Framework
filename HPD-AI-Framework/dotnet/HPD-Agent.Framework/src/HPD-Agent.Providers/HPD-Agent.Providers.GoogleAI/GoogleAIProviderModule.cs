using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Auto-discovers and registers the Google AI provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class GoogleAIProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderDiscovery.RegisterProviderFactory(() => new GoogleAIProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        ProviderDiscovery.RegisterProviderConfigType<GoogleAIProviderConfig>(
            "google-ai",
            json => JsonSerializer.Deserialize(json, GoogleAIJsonContext.Default.GoogleAIProviderConfig),
            config => JsonSerializer.Serialize(config, GoogleAIJsonContext.Default.GoogleAIProviderConfig));

        // Register environment variable aliases for secret resolution
        SecretAliasRegistry.Register("google-ai:ApiKey", "GOOGLE_API_KEY", "GEMINI_API_KEY");
    }
}
