using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.Anthropic;

/// <summary>
/// Auto-discovers and registers the Anthropic provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class AnthropicProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderDiscovery.RegisterProviderFactory(() => new AnthropicProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        ProviderDiscovery.RegisterProviderConfigType<AnthropicProviderConfig>(
            "anthropic",
            json => JsonSerializer.Deserialize(json, AnthropicJsonContext.Default.AnthropicProviderConfig),
            config => JsonSerializer.Serialize(config, AnthropicJsonContext.Default.AnthropicProviderConfig));

        // Register environment variable aliases for secret resolution
        SecretAliasRegistry.Register("anthropic:ApiKey", "ANTHROPIC_API_KEY");
    }
}
