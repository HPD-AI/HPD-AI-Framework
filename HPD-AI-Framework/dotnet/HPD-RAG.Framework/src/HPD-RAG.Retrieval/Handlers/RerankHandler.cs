using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Providers.Reranker;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Retrieval.Handlers;

/// <summary>
/// Reranks search results using the keyed IReranker, trimming to TopN.
/// Default retry: 3 attempts, JitteredExponential, 2–60s.
/// Default propagation: SkipDependents — passes through un-reranked results when the reranker is unavailable.
/// </summary>
[GraphNodeHandler(NodeName = "Rerank")]
public sealed partial class RerankHandler : IGraphNodeHandler<MragPipelineContext>
{
    public static MragRetryPolicy DefaultRetryPolicy { get; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(2),
        Strategy = MragBackoffStrategy.JitteredExponential,
        MaxDelay = TimeSpan.FromSeconds(60)
    };

    public static MragErrorPropagation DefaultErrorPropagation { get; } = MragErrorPropagation.SkipDependents;

    public sealed class Config
    {
        public int TopN { get; set; } = 5;
    }

    public async Task<RerankOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "The original query string used for relevance scoring.")] string Query,
        [InputSocket(Description = "The candidate results to rerank.")] MragSearchResultDto[] Results,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();
        var reranker = context.Services.GetRequiredKeyedService<IReranker>("mrag:rerank");

        var reranked = await reranker
            .RerankAsync(Query, Results, config.TopN, cancellationToken)
            .ConfigureAwait(false);

        return new RerankOutput { Results = reranked.ToArray() };
    }

    public sealed class RerankOutput
    {
        [OutputSocket(Description = "Reranked results, sorted descending by score, trimmed to TopN.")]
        public required MragSearchResultDto[] Results { get; init; }
    }
}
