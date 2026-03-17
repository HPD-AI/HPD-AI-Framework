using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;

namespace HPD.RAG.Retrieval.Handlers;

/// <summary>
/// Merges multiple result sets from parallel search branches into one deduplicated list.
/// Deduplication key: DocumentId. Score aggregation strategy: max score wins on collision.
/// Output is sorted descending by score.
/// Pure in-process merge — no external calls.
/// Default retry: 1 attempt (MragRetryPolicy.None).
/// Default propagation: StopPipeline.
/// </summary>
[GraphNodeHandler(NodeName = "MergeResults")]
public sealed partial class MergeResultsHandler : IGraphNodeHandler<MragPipelineContext>
{
    public static MragRetryPolicy DefaultRetryPolicy { get; } = MragRetryPolicy.None;

    public static MragErrorPropagation DefaultErrorPropagation { get; } = MragErrorPropagation.StopPipeline;

    public Task<MergeResultsOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "Array of result sets to merge (e.g., from parallel search branches).")] MragSearchResultDto[][] ResultSets,
        CancellationToken cancellationToken = default)
    {
        // Deduplicate by DocumentId, keeping the highest score for each document.
        var best = new Dictionary<string, MragSearchResultDto>(StringComparer.Ordinal);

        foreach (var resultSet in ResultSets)
        {
            foreach (var result in resultSet)
            {
                if (!best.TryGetValue(result.DocumentId, out var existing) || result.Score > existing.Score)
                {
                    best[result.DocumentId] = result;
                }
            }
        }

        var merged = best.Values
            .OrderByDescending(r => r.Score)
            .ToArray();

        return Task.FromResult(new MergeResultsOutput { Results = merged });
    }

    public sealed class MergeResultsOutput
    {
        [OutputSocket(Description = "Deduplicated, score-sorted merged results.")]
        public required MragSearchResultDto[] Results { get; init; }
    }
}
