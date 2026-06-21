using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.HuggingFace;

/// <summary>
/// Provider descriptor for the HuggingFace TEI reranker.
/// Registered automatically via <see cref="HuggingFaceRerankerModule"/>.
/// </summary>
public sealed class HuggingFaceRerankerFeatures : IRerankerFeatures
{
    public string ProviderKey => "huggingface";
    public string DisplayName => "HuggingFace TEI Reranker";

    public IReranker CreateReranker(RerankerConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var httpClient = new HttpClient();
        return new HuggingFaceReranker(httpClient, config);
    }
}
