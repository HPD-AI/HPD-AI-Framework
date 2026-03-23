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
/// Performs vector similarity search against the keyed VectorStore.
/// Applies a filter only when the Filter socket is connected; otherwise the search runs unfiltered.
///
/// Filter translation: the filter AST is compiled to a backend-native representation via
/// IVectorStoreFeatures.CreateFilterTranslator().Translate(). Backends that return a
/// VectorSearchFilter have it applied via VectorSearchOptions.OldFilter. Backends returning
/// other opaque types must apply the filter inside their own collection implementation.
///
/// Default retry: 3 attempts, JitteredExponential, 1-30s.
/// Default propagation: StopPipeline.
/// </summary>
[GraphNodeHandler(NodeName = "VectorSearch")]
public sealed partial class VectorSearchHandler : IGraphNodeHandler<MragPipelineContext>
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
        /// <summary>Embedding vector dimensionality. Must match the model used during ingestion.</summary>
        public int Dimensions { get; set; } = 1536;
    }

    public async Task<VectorSearchOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "The query embedding vector.")] float[] Embedding,
        [InputSocket(Optional = true, Description = "Optional filter to narrow results.")] MragFilterNode? Filter,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();
        var collectionName = config.CollectionName
            ?? context.CollectionName
            ?? throw new InvalidOperationException(
                "VectorSearchHandler: CollectionName must be set in node Config or MragPipelineContext.");

        var vectorStore = context.Services.GetRequiredKeyedService<VectorStore>("mrag:vectorstore");
        var features = context.Services.GetRequiredKeyedService<IVectorStoreFeatures>("mrag:vectorstore-features");

        // Only call the translator when the Filter socket is connected.
        // VectorSearchFilter is the legacy/compat filter API in ME.VectorData v9.
        VectorSearchFilter? vsf = null;
        if (Filter is not null)
        {
            vsf = features.CreateFilterTranslator().Translate(Filter) as VectorSearchFilter;
        }

        var definition = new VectorStoreCollectionDefinition
        {
            Properties =
            [
                new VectorStoreKeyProperty("DocumentId", typeof(string)),
                new VectorStoreDataProperty("Content", typeof(string)) { IsFullTextIndexed = true },
                new VectorStoreDataProperty("Context", typeof(string)),
                new VectorStoreDataProperty("Metadata", typeof(Dictionary<string, System.Text.Json.JsonElement>)),
                new VectorStoreVectorProperty("Embedding", typeof(ReadOnlyMemory<float>), config.Dimensions)
            ]
        };

        var collection = vectorStore.GetCollection<string, MragVectorRecord>(collectionName, definition);

#pragma warning disable CS0618 // OldFilter is the VectorSearchFilter compat path in v9
        var searchOptions = new VectorSearchOptions<MragVectorRecord>
        {
            OldFilter = vsf
        };
#pragma warning restore CS0618

        var results = new List<MragSearchResultDto>();
        await foreach (var item in collection.SearchAsync(
            new ReadOnlyMemory<float>(Embedding),
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

        return new VectorSearchOutput { Results = results.ToArray() };
    }

    public sealed class VectorSearchOutput
    {
        [OutputSocket(Description = "Ranked search results from the vector store.")]
        public required MragSearchResultDto[] Results { get; init; }
    }
}
