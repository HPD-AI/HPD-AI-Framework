using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.Bedrock;

/// <summary>
/// Auto-discovers and registers the AWS Bedrock embedding provider on assembly load.
/// Also registers the typed config for AOT-compatible JSON serialization.
/// </summary>
public static class BedrockEmbeddingProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        EmbeddingDiscovery.RegisterEmbeddingProviderFactory(() => new BedrockEmbeddingProviderFeatures());

        EmbeddingDiscovery.RegisterEmbeddingConfigType<BedrockEmbeddingConfig>(
            "bedrock",
            json => JsonSerializer.Deserialize(json, BedrockEmbeddingJsonContext.Default.BedrockEmbeddingConfig),
            cfg => JsonSerializer.Serialize(cfg, BedrockEmbeddingJsonContext.Default.BedrockEmbeddingConfig));
    }
}
