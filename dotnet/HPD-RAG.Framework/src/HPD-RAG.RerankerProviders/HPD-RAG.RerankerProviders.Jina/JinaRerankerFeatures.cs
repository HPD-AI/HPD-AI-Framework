using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.Jina;

/// <summary>
/// Provider descriptor for the Jina AI reranker.
/// Registered automatically via <see cref="JinaRerankerModule"/>.
/// </summary>
public sealed class JinaRerankerFeatures : IRerankerFeatures
{
    public string ProviderKey => "jina";
    public string DisplayName => "Jina AI Reranker";

    public IReranker CreateReranker(RerankerConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var httpClient = new HttpClient();
        return new JinaReranker(httpClient, config);
    }
}
