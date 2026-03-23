using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Extensions;

/// <summary>
/// Fluent builder returned by <see cref="MragServiceCollectionExtensions.AddHPDMrag"/>.
/// Use this to configure the handler catalogs for ingestion, retrieval, and evaluation.
/// </summary>
public sealed class MragBuilder
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; }

    internal MragBuilder(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Configures and registers the ingestion handler catalog (readers, chunkers, enrichers, vector writers).
    /// Calls <c>AddGeneratedMragPipelineContextHandlers()</c> on the service collection at the end.
    /// </summary>
    public MragBuilder AddIngestionHandlers(Action<MragIngestionHandlersBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MragIngestionHandlersBuilder(Services);
        configure(builder);
        builder.Complete();
        return this;
    }

    /// <summary>
    /// Configures and registers the retrieval handler catalog (embed, search, rerank, format, query transforms).
    /// Calls <c>AddGeneratedMragPipelineContextHandlers()</c> on the service collection at the end.
    /// </summary>
    public MragBuilder AddRetrievalHandlers(Action<MragRetrievalHandlersBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MragRetrievalHandlersBuilder(Services);
        configure(builder);
        builder.Complete();
        return this;
    }

    /// <summary>
    /// Configures and registers the evaluation handler catalog (evaluators, result storage).
    /// Calls <c>AddGeneratedMragPipelineContextHandlers()</c> on the service collection at the end.
    /// </summary>
    public MragBuilder AddEvaluationHandlers(Action<MragEvaluationHandlersBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new MragEvaluationHandlersBuilder(Services);
        configure(builder);
        builder.Complete();
        return this;
    }
}
