using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.Jina;

/// <summary>
/// Auto-registers the Jina AI reranker provider on assembly load.
/// </summary>
public static class JinaRerankerModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        RerankerDiscovery.RegisterRerankerFactory(() => new JinaRerankerFeatures());

        RerankerDiscovery.RegisterRerankerConfigType<JinaRerankerConfig>(
            "jina",
            json => JsonSerializer.Deserialize(json, JinaJsonContext.Default.JinaRerankerConfig),
            config => JsonSerializer.Serialize(config, JinaJsonContext.Default.JinaRerankerConfig));
    }
}
