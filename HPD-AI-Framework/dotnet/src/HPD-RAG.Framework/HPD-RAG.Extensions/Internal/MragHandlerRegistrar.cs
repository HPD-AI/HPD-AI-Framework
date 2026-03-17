using HPD.RAG.Core.Context;
using HPD.RAG.Evaluation.Handlers;
using HPD.RAG.Ingestion.Chunkers;
using HPD.RAG.Ingestion.Enrichers;
using HPD.RAG.Ingestion.Processors;
using HPD.RAG.Ingestion.Readers;
using HPD.RAG.Ingestion.Writers;
using HPD.RAG.Retrieval.Handlers;
using HPDAgent.Graph.Abstractions.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Extensions.Internal;

/// <summary>
/// Registers all known MRAG handler types into DI as
/// <c>IGraphNodeHandler&lt;MragPipelineContext&gt;</c>.
///
/// <para>
/// This manual registration approach is used instead of relying on
/// <c>AddGeneratedMragPipelineContextHandlers()</c> from the HPD.Graph source generator because:
/// <list type="bullet">
/// <item>The <c>DIRegistrationGenerator</c> only finds handlers that explicitly implement
/// <c>IGraphNodeHandler&lt;T&gt;</c> in source-visible code. Ingestion handlers use
/// the <c>SocketBridgeGenerator</c> to generate the interface implementation, which
/// is not visible to <c>DIRegistrationGenerator</c> during the same compilation pass.</item>
/// <item>Even when the generator does run for Retrieval/Evaluation, its output is in a class
/// <c>HPDAgent.Graph.Extensions.GeneratedHandlerRegistration</c> that conflicts across
/// assemblies, requiring <c>extern alias</c> which is fragile in this multi-project setup.</item>
/// </list>
/// </para>
/// </summary>
internal static class MragHandlerRegistrar
{
    private static readonly object _ingestionLock = new();
    private static readonly object _retrievalLock = new();
    private static readonly object _evaluationLock = new();

    /// <summary>
    /// Registers all ingestion handler types (readers, chunkers, enrichers, writers).
    /// Idempotent: calling multiple times on the same <paramref name="services"/> is safe.
    /// </summary>
    internal static void RegisterIngestionHandlers(IServiceCollection services)
    {
        // Document readers
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, MarkdownReaderHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, MarkItDownReaderHandler>();

        // Chunkers
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, HeaderChunkerHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, SectionChunkerHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, TokenChunkerHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, SemanticChunkerHandler>();

        // Enrichers
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, KeywordEnricherHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, SummaryEnricherHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, SentimentEnricherHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, ClassificationEnricherHandler>();

        // Processors
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, ImageEnricherHandler>();

        // Vector store writers
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, InMemoryWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, PostgresWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, QdrantWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, WeaviateWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, PineconeWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, RedisWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, AzureAISearchWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, MongoWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, CosmosMongoWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, CosmosNoSqlWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, SqlServerWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, SqliteWriterHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, MilvusWriterHandler>();
    }

    /// <summary>
    /// Registers all retrieval handler types.
    /// </summary>
    internal static void RegisterRetrievalHandlers(IServiceCollection services)
    {
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, EmbedQueryHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, VectorSearchHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, HybridSearchHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, RerankHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, FormatContextHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, GenerateHypotheticalHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, DecomposeQueryHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, MergeResultsHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, GraphRetrieverHandler>();
    }

    /// <summary>
    /// Registers all evaluation handler types.
    /// </summary>
    internal static void RegisterEvaluationHandlers(IServiceCollection services)
    {
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, RelevanceEvalHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, GroundednessEvalHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, FluencyEvalHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, CompletenessEvalHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, BLEUEvalHandler>();
        services.AddScoped<IGraphNodeHandler<MragPipelineContext>, WriteEvalResultHandler>();
    }
}
