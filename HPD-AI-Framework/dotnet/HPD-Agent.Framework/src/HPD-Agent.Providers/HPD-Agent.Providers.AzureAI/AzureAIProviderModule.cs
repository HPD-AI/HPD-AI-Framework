using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.AzureAI;

/// <summary>
/// Auto-discovers and registers the Azure AI provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class AzureAIProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderDiscovery.RegisterProviderFactory(() => new AzureAIProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        ProviderDiscovery.RegisterProviderConfigType<AzureAIProviderConfig>(
            "azure-ai",
            json => JsonSerializer.Deserialize(json, AzureAIJsonContext.Default.AzureAIProviderConfig),
            config => JsonSerializer.Serialize(config, AzureAIJsonContext.Default.AzureAIProviderConfig));

        // Register environment variable aliases
        SecretAliasRegistry.Register("azure-ai:ApiKey", "AZURE_AI_API_KEY");
        SecretAliasRegistry.Register("azure-ai:Endpoint", "AZURE_AI_ENDPOINT");
    }
}
