using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.Cohere;

/// <summary>
/// Provider descriptor for the Cohere reranker.
/// Registered automatically via <see cref="CohereRerankerModule"/>.
/// </summary>
public sealed class CohereRerankerFeatures : IRerankerFeatures
{
    public string ProviderKey => "cohere";
    public string DisplayName => "Cohere Rerank";

    public IReranker CreateReranker(RerankerConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var httpClient = new HttpClient();
        return new CohereReranker(httpClient, config);
    }
}
