using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.Cohere;

/// <summary>
/// Auto-registers the Cohere reranker provider on assembly load.
/// </summary>
public static class CohereRerankerModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        RerankerDiscovery.RegisterRerankerFactory(() => new CohereRerankerFeatures());

        RerankerDiscovery.RegisterRerankerConfigType<CohereRerankerConfig>(
            "cohere",
            json => JsonSerializer.Deserialize(json, CohereJsonContext.Default.CohereRerankerConfig),
            config => JsonSerializer.Serialize(config, CohereJsonContext.Default.CohereRerankerConfig));
    }
}
