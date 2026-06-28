using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Auto-discovers and registers OpenAI providers on assembly load.
/// Also registers the provider-specific config type for FFI/JSON serialization.
/// </summary>
public static class OpenAIProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        // Register provider factories
        ProviderDiscovery.RegisterProviderFactory(() => new OpenAIProvider());
        ProviderDiscovery.RegisterProviderFactory(() => new AzureOpenAIProvider());

        // Register config types for FFI/JSON serialization (AOT-compatible)
        ProviderDiscovery.RegisterProviderConfigType<OpenAIProviderConfig>(
            "openai",
            json => JsonSerializer.Deserialize(json, OpenAIJsonContext.Default.OpenAIProviderConfig),
            config => JsonSerializer.Serialize(config, OpenAIJsonContext.Default.OpenAIProviderConfig));

        ProviderDiscovery.RegisterProviderConfigType<AzureOpenAIProviderConfig>(
            "azure-openai",
            json => JsonSerializer.Deserialize(json, OpenAIJsonContext.Default.AzureOpenAIProviderConfig),
            config => JsonSerializer.Serialize(config, OpenAIJsonContext.Default.AzureOpenAIProviderConfig));

        // Register environment variable aliases for unified secret resolution
        SecretAliasRegistry.Register("openai:ApiKey", "OPENAI_API_KEY");
        SecretAliasRegistry.Register("azure-openai:ApiKey", "AZURE_OPENAI_API_KEY");
        SecretAliasRegistry.Register("azure-openai:Endpoint", "AZURE_OPENAI_ENDPOINT");
    }
}
