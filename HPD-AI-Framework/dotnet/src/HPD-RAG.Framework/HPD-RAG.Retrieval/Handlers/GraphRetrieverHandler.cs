using System.Text.RegularExpressions;
using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPD.RAG.Core.Providers.GraphStore;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Retrieval.Handlers;

/// <summary>
/// Retrieves a subgraph from the keyed IGraphStore starting from seed entity IDs.
/// When SeedEntityIds is null, extracts candidate entity keywords from the Query.
/// Default retry: 3 attempts, JitteredExponential, 1-30s.
/// Default propagation: StopPipeline.
/// </summary>
[GraphNodeHandler(NodeName = "GraphRetrieve")]
public sealed partial class GraphRetrieverHandler : IGraphNodeHandler<MragPipelineContext>
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
        public int MaxDepth { get; set; } = 2;
        public int Limit { get; set; } = 30;
    }

    public async Task<GraphRetrieverOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "The natural-language query; used for entity extraction when SeedEntityIds is null.")] string Query,
        [InputSocket(Optional = true, Description = "Explicit seed entity IDs to start graph traversal from.")] string[]? SeedEntityIds,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();
        var graphStore = context.Services.GetRequiredKeyedService<IGraphStore>("mrag:graph");

        IReadOnlyList<string> seeds = SeedEntityIds is { Length: > 0 }
            ? SeedEntityIds
            : ExtractKeywords(Query);

        var graph = await graphStore
            .GetRelationshipsAsync(seeds, config.MaxDepth, config.Limit, cancellationToken)
            .ConfigureAwait(false);

        return new GraphRetrieverOutput { Graph = graph };
    }

    /// <summary>
    /// Lightweight keyword extraction used when no SeedEntityIds are provided.
    /// Splits on whitespace/punctuation, removes common English stop words, deduplicates.
    /// This is a heuristic fallback -- replace with an NER model or LLM call for production accuracy.
    /// </summary>
    internal static IReadOnlyList<string> ExtractKeywords(string query)
    {
        var tokens = WordTokenizerRegex().Split(query);
        return tokens
            .Where(t => t.Length > 2 && !StopWords.Contains(t.ToLowerInvariant()))
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
        "of", "with", "by", "from", "up", "about", "into", "through", "during",
        "is", "are", "was", "were", "be", "been", "being", "have", "has", "had",
        "do", "does", "did", "will", "would", "could", "should", "may", "might",
        "can", "not", "no", "nor", "so", "yet", "both", "either", "neither",
        "such", "that", "this", "these", "those", "its", "it", "they", "them",
        "their", "what", "which", "who", "whom", "when", "where", "why", "how"
    };

    [GeneratedRegex(@"[\s\p{P}]+")]
    private static partial Regex WordTokenizerRegex();

    public sealed class GraphRetrieverOutput
    {
        [OutputSocket(Description = "Subgraph containing nodes and edges reachable from the seed entities.")]
        public required MragGraphResultDto Graph { get; init; }
    }
}
