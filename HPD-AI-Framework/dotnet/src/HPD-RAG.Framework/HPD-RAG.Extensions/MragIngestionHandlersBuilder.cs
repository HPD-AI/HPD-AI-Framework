using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.Extensions.Internal;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Extensions;

/// <summary>
/// Fluent builder for configuring ingestion handlers.
/// Each <c>Use*</c> call registers the corresponding handler type and its required keyed services.
/// </summary>
public sealed class MragIngestionHandlersBuilder
{
    private readonly IServiceCollection _services;

    internal MragIngestionHandlersBuilder(IServiceCollection services)
    {
        _services = services;
    }

    // ── Document Readers ──────────────────────────────────────────────── //

    /// <summary>
    /// Marks <c>MarkItDownReaderHandler</c> ("ReadDocuments") for registration.
    /// All handlers are registered in bulk via <see cref="Complete"/>.
    /// </summary>
    public MragIngestionHandlersBuilder UseMarkItDownReader() => this;

    /// <summary>
    /// Marks <c>MarkdownReaderHandler</c> ("ReadMarkdown") for registration.
    /// All handlers are registered in bulk via <see cref="Complete"/>.
    /// </summary>
    public MragIngestionHandlersBuilder UseMarkdownReader() => this;

    // ── Chunkers ─────────────────────────────────────────────────────── //

    /// <summary>Marks <c>HeaderChunkerHandler</c> ("ChunkByHeader") for registration.</summary>
    public MragIngestionHandlersBuilder UseHeaderChunker() => this;

    /// <summary>Marks <c>SectionChunkerHandler</c> ("ChunkBySection") for registration.</summary>
    public MragIngestionHandlersBuilder UseSectionChunker() => this;

    /// <summary>Marks <c>TokenChunkerHandler</c> ("ChunkByToken") for registration.</summary>
    public MragIngestionHandlersBuilder UseTokenChunker() => this;

    /// <summary>
    /// Marks <c>SemanticChunkerHandler</c> ("ChunkSemantic") for registration.
    /// Requires <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> keyed
    /// <c>"mrag:embedding"</c> to be registered (via <see cref="UseEmbeddingProvider"/>).
    /// The missing dependency is detected at DI resolve time, not at registration time.
    /// </summary>
    public MragIngestionHandlersBuilder UseSemanticChunker() => this;

    // ── Chunk Enrichers ───────────────────────────────────────────────── //

    /// <summary>
    /// Configures <c>KeywordEnricherHandler</c> and registers the <c>IChatClient</c>
    /// keyed <c>"mrag:enricher:keywords"</c>.
    /// </summary>
    public MragIngestionHandlersBuilder UseKeywordEnricher(Action<EnricherClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new EnricherClientBuilder("mrag:enricher:keywords", _services);
        configure(builder);
        builder.Register();
        return this;
    }

    /// <summary>
    /// Configures <c>SummaryEnricherHandler</c> and registers the <c>IChatClient</c>
    /// keyed <c>"mrag:enricher:summary"</c>.
    /// </summary>
    public MragIngestionHandlersBuilder UseSummaryEnricher(Action<EnricherClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new EnricherClientBuilder("mrag:enricher:summary", _services);
        configure(builder);
        builder.Register();
        return this;
    }

    /// <summary>
    /// Configures <c>SentimentEnricherHandler</c> and registers the <c>IChatClient</c>
    /// keyed <c>"mrag:enricher:sentiment"</c>.
    /// </summary>
    public MragIngestionHandlersBuilder UseSentimentEnricher(Action<EnricherClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new EnricherClientBuilder("mrag:enricher:sentiment", _services);
        configure(builder);
        builder.Register();
        return this;
    }

    /// <summary>
    /// Configures <c>ClassificationEnricherHandler</c> and registers the <c>IChatClient</c>
    /// keyed <c>"mrag:enricher:classification"</c>.
    /// </summary>
    public MragIngestionHandlersBuilder UseClassificationEnricher(Action<EnricherClientBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new EnricherClientBuilder("mrag:enricher:classification", _services);
        configure(builder);
        builder.Register();
        return this;
    }

    // ── Embedding Provider ────────────────────────────────────────────── //

    /// <summary>
    /// Resolves <paramref name="providerKey"/> via <see cref="EmbeddingDiscovery"/>, creates an
    /// <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c>, and registers it keyed
    /// <c>"mrag:embedding"</c>.
    /// </summary>
    /// <param name="providerKey">Provider key registered via <c>EmbeddingDiscovery</c> (e.g. <c>"openai"</c>).</param>
    /// <param name="modelName">The embedding model name.</param>
    /// <param name="configure">Optional callback to customize <see cref="EmbeddingConfig"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown at resolve time if <paramref name="providerKey"/> is not registered.
    /// </exception>
    public MragIngestionHandlersBuilder UseEmbeddingProvider(
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
    public MragIngestionHandlersBuilder RegisterVectorStore(Action<VectorStoreBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new VectorStoreBuilder(_services);
        configure(builder);
        builder.Register();
        return this;
    }

    /// <summary>Called by <see cref="MragBuilder"/> after configure runs to finalize handler registration.</summary>
    internal void Complete()
    {
        MragHandlerRegistrar.RegisterIngestionHandlers(_services);
    }
}
