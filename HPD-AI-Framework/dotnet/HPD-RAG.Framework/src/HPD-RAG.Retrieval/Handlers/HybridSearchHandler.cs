using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.Retrieval.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Retrieval.Handlers;

/// <summary>
/// Performs hybrid (keyword + vector) search using IKeywordHybridSearchable from the resolved
/// VectorStore collection. Throws InvalidOperationException if the collection does not implement
/// IKeywordHybridSearchable.
/// Default retry: 3 attempts, JitteredExponential, 1-30s.
/// Default propagation: StopPipeline.
/// </summary>
[GraphNodeHandler(NodeName = "HybridSearch")]
public sealed partial class HybridSearchHandler : IGraphNodeHandler<MragPipelineContext>
{
    public static MragRetryPolicy DefaultRetryPolicy { get; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        Strategy = MragBackoffStrategy.JitteredExponential,
        MaxDelay = TimeSpan.FromSeconds(30)
    };

    public static MragErrorPropagation DefaultErrorPropagation { get; } = MragErrorPropagation.StopPipeline;

    public sealed class Config
    {
        public string? CollectionName { get; set; }
        public int TopK { get; set; } = 10;
    }

    public async Task<HybridSearchOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "The raw query string for keyword + vector hybrid search.")] string Query,
        [InputSocket(Optional = true, Description = "Optional filter to narrow results.")] MragFilterNode? Filter,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();
        var collectionName = config.CollectionName
            ?? context.CollectionName
            ?? throw new InvalidOperationException(
                "HybridSearchHandler: CollectionName must be set in node Config or MragPipelineContext.");

        var vectorStore = context.Services.GetRequiredKeyedService<VectorStore>("mrag:vectorstore");
        var features = context.Services.GetRequiredKeyedService<IVectorStoreFeatures>("mrag:vectorstore-features");

        // Only call the translator when the Filter socket is connected.
        VectorSearchFilter? vsf = null;
        if (Filter is not null)
        {
            vsf = features.CreateFilterTranslator().Translate(Filter) as VectorSearchFilter;
        }

        var collection = vectorStore.GetCollection<string, MragVectorRecord>(collectionName);

        if (collection is not IKeywordHybridSearchable<MragVectorRecord> hybridCollection)
        {
            throw new InvalidOperationException(
                $"HybridSearchHandler: The resolved VectorStore collection for '{collectionName}' does not implement " +
                "IKeywordHybridSearchable<MragVectorRecord>. Use a backend that supports hybrid search or replace with VectorSearchHandler.");
        }

#pragma warning disable CS0618 // OldFilter is the VectorSearchFilter compat path in v9
        var searchOptions = new HybridSearchOptions<MragVectorRecord>
        {
            OldFilter = vsf
        };
#pragma warning restore CS0618

        var results = new List<MragSearchResultDto>();
        await foreach (var item in hybridCollection.HybridSearchAsync(
            Query,
            [Query],
            config.TopK,
            searchOptions,
            cancellationToken).ConfigureAwait(false))
        {
            results.Add(new MragSearchResultDto
            {
                DocumentId = item.Record.DocumentId,
                Content = item.Record.Content,
                Context = item.Record.Context,
                Score = item.Score ?? 0.0,
                Metadata = item.Record.Metadata
            });
        }

        return new HybridSearchOutput { Results = results.ToArray() };
    }

    public sealed class HybridSearchOutput
    {
        [OutputSocket(Description = "Ranked hybrid search results.")]
        public required MragSearchResultDto[] Results { get; init; }
    }
}
