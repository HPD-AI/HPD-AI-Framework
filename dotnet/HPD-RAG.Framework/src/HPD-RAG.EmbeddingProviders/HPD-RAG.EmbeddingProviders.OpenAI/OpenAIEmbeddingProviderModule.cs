using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.OpenAI;

/// <summary>
/// Auto-discovers and registers the OpenAI embedding provider on assembly load.
/// </summary>
public static class OpenAIEmbeddingProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        EmbeddingDiscovery.RegisterEmbeddingProviderFactory(() => new OpenAIEmbeddingProviderFeatures());

        EmbeddingDiscovery.RegisterEmbeddingConfigType<OpenAIEmbeddingConfig>(
            "openai",
            json => JsonSerializer.Deserialize(json, OpenAIEmbeddingJsonContext.Default.OpenAIEmbeddingConfig),
            config => JsonSerializer.Serialize(config, OpenAIEmbeddingJsonContext.Default.OpenAIEmbeddingConfig));
    }
}
