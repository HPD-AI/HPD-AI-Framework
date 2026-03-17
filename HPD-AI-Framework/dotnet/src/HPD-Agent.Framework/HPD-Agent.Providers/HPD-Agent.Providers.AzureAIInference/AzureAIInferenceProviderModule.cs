using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.AzureAIInference;

/// <summary>
/// Auto-discovers and registers the Azure AI Inference provider on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class AzureAIInferenceProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factory
        ProviderDiscovery.RegisterProviderFactory(() => new AzureAIInferenceProvider());

        // Register config type for FFI/JSON serialization (AOT-compatible)
        // Note: The config serialization is AOT-ready, but the Azure AI Inference SDK itself is not AOT compatible
        ProviderDiscovery.RegisterProviderConfigType<AzureAIInferenceProviderConfig>(
            "azure-ai-inference",
            json => JsonSerializer.Deserialize(json, AzureAIInferenceJsonContext.Default.AzureAIInferenceProviderConfig),
            config => JsonSerializer.Serialize(config, AzureAIInferenceJsonContext.Default.AzureAIInferenceProviderConfig));

        // Register environment variable aliases
        SecretAliasRegistry.Register("azure-ai-inference:ApiKey", "AZURE_AI_INFERENCE_API_KEY");
        SecretAliasRegistry.Register("azure-ai-inference:Endpoint", "AZURE_AI_INFERENCE_ENDPOINT");
    }
}
