using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.AzureAI;

/// <summary>
/// Auto-discovers and registers the AzureAI embedding provider on assembly load.
/// Also registers the typed config for AOT-compatible JSON serialization.
/// </summary>
public static class AzureAIEmbeddingProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        EmbeddingDiscovery.RegisterEmbeddingProviderFactory(() => new AzureAIEmbeddingProviderFeatures());

        EmbeddingDiscovery.RegisterEmbeddingConfigType<AzureAIEmbeddingConfig>(
            "azureai",
            json => JsonSerializer.Deserialize(json, AzureAIEmbeddingJsonContext.Default.AzureAIEmbeddingConfig),
            cfg => JsonSerializer.Serialize(cfg, AzureAIEmbeddingJsonContext.Default.AzureAIEmbeddingConfig));
    }
}
