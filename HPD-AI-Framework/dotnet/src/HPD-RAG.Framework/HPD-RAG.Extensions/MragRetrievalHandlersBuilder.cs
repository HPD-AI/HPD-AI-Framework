using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.Core.Providers.GraphStore;
using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.Extensions.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Extensions;

/// <summary>
/// Fluent builder for configuring retrieval handlers.
/// </summary>
public sealed class MragRetrievalHandlersBuilder
{
    private readonly IServiceCollection _services;

    internal MragRetrievalHandlersBuilder(IServiceCollection services)
    {
        _services = services;
    }

    // ── Embedding Provider ────────────────────────────────────────────── //

    /// <summary>
    /// Resolves <paramref name="providerKey"/> via <see cref="EmbeddingDiscovery"/> and registers
    /// <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> keyed <c>"mrag:embedding"</c>.
    /// </summary>
    public MragRetrievalHandlersBuilder UseEmbeddingProvider(
        string providerKey,
        string modelName,
        Action<EmbeddingConfig>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(providerKey);
        ArgumentNullException.ThrowIfNull(modelName);

        var config = new EmbeddingConfig { ProviderKey = providerKey, ModelName = modelName };
        configure?.Invoke(config);

        _services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            "mrag:embedding",
            (sp, _) =>
            {
                var features = EmbeddingDiscovery.GetProvider(providerKey)
                    ?? throw new InvalidOperationException(
                        $"No embedding provider registered for key '{providerKey}'. " +
                        $"Ensure the corresponding HPD.RAG.EmbeddingProviders.* package is referenced.");
                return features.CreateEmbeddingGenerator(config, sp);
            });

        return this;
    }

    // ── Vector Store ──────────────────────────────────────────────────── //

    /// <summary>
    /// Configures a vector store via <see cref="VectorStoreBuilder"/> and registers both
    /// <c>VectorStore</c> keyed <c>"mrag:vectorstore"</c> and <c>IVectorStoreFeatures</c> keyed
    /// <c>"mrag:vectorstore-features"</c>.
    /// </summary>
    public MragRetrievalHandlersBuilder UseVectorStore(Action<VectorStoreBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new VectorStoreBuilder(_services);
        configure(builder);
        builder.Register();
        return this;
    }

    // ── Query Transform Models ────────────────────────────────────────── //

    /// <summary>
    /// Registers an <c>IChatClient</c> keyed <c>"mrag:hypothetical"</c> used by
    /// <c>GenerateHypotheticalHandler</c>.
    /// </summary>
    public MragRetrievalHandlersBuilder UseHypotheticalModel(Func<IServiceProvider, IChatClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _services.AddKeyedSingleton<IChatClient>("mrag:hypothetical", (sp, _) => factory(sp));
        return this;
    }

    /// <summary>
    /// Registers an <c>IChatClient</c> keyed <c>"mrag:decompose"</c> used by
    /// <c>DecomposeQueryHandler</c>.
    /// </summary>
    public MragRetrievalHandlersBuilder UseDecomposeModel(Func<IServiceProvider, IChatClient> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _services.AddKeyedSingleton<IChatClient>("mrag:decompose", (sp, _) => factory(sp));
        return this;
    }

    // ── Reranker ─────────────────────────────────────────────────────── //

    /// <summary>
    /// Configures a reranker via <see cref="RerankerBuilder"/> and registers it as
    /// <c>IReranker</c> keyed <c>"mrag:rerank"</c>.
    /// </summary>
    public MragRetrievalHandlersBuilder UseReranker(Action<RerankerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new RerankerBuilder(_services);
        configure(builder);
        builder.Register();
        return this;
    }

    // ── Graph Store ───────────────────────────────────────────────────── //

    /// <summary>
    /// Configures a graph store via <see cref="GraphStoreBuilder"/> and registers it as
    /// <c>IGraphStore</c> keyed <c>"mrag:graph"</c>.
    /// </summary>
    public MragRetrievalHandlersBuilder UseGraphStore(Action<GraphStoreBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new GraphStoreBuilder(_services);
        configure(builder);
        builder.Register();
        return this;
    }

    /// <summary>Called by <see cref="MragBuilder"/> after configure runs to finalize handler registration.</summary>
    internal void Complete()
    {
        MragHandlerRegistrar.RegisterRetrievalHandlers(_services);
    }
}
