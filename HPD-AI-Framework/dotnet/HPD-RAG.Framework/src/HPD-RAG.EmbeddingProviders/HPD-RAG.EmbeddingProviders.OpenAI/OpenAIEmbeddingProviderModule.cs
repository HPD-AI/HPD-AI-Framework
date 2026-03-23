using System.Runtime.CompilerServices;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.OpenAI;

/// <summary>
/// Auto-discovers and registers the OpenAI embedding provider on assembly load.
/// OpenAI uses only base EmbeddingConfig fields — no typed config registration needed.
/// </summary>
public static class OpenAIEmbeddingProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        EmbeddingDiscovery.RegisterEmbeddingProviderFactory(() => new OpenAIEmbeddingProviderFeatures());
        // OpenAI: no typed config registration — ModelName + ApiKey from base EmbeddingConfig are sufficient.
    }
}
