namespace HPD.RAG.Core.Providers.Reranker;

/// <summary>
/// Core abstraction implemented by every HPD.RAG.RerankerProviders.* package.
/// Mirrors IVectorStoreFeatures exactly.
/// </summary>
public interface IRerankerFeatures
{
    string ProviderKey { get; }
    string DisplayName { get; }
    IReranker CreateReranker(RerankerConfig config, IServiceProvider? services = null);
}
