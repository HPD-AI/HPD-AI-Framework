using Microsoft.Extensions.AI;

namespace HPD.RAG.Core.Providers.Embedding;

/// <summary>
/// Core abstraction implemented by every HPD.RAG.EmbeddingProviders.* package.
/// Intentionally separate from IProviderFeatures — embedding and chat client lifecycles,
/// middleware stacks, and config concerns are independent.
/// </summary>
public interface IEmbeddingProviderFeatures
{
    string ProviderKey { get; }
    string DisplayName { get; }

    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingConfig config, IServiceProvider? services = null);
}
