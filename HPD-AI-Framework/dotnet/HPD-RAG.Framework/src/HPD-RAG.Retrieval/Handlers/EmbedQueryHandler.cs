using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Retrieval.Handlers;

/// <summary>
/// Embeds a query string into a float[] vector using the keyed IEmbeddingGenerator.
/// Default retry: 3 attempts, JitteredExponential, 1-30s.
/// Default propagation: StopPipeline.
/// </summary>
[GraphNodeHandler(NodeName = "EmbedQuery")]
public sealed partial class EmbedQueryHandler : IGraphNodeHandler<MragPipelineContext>
{
    public static MragRetryPolicy DefaultRetryPolicy { get; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        Strategy = MragBackoffStrategy.JitteredExponential,
        MaxDelay = TimeSpan.FromSeconds(30)
    };

    public static MragErrorPropagation DefaultErrorPropagation { get; } = MragErrorPropagation.StopPipeline;

    public async Task<EmbedQueryOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "The query string to embed.")] string Query,
        CancellationToken cancellationToken = default)
    {
        var generator = context.Services
            .GetRequiredKeyedService<IEmbeddingGenerator<string, Embedding<float>>>("mrag:embedding");

        var result = await generator
            .GenerateAsync([Query], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new EmbedQueryOutput { Embedding = result[0].Vector.ToArray() };
    }

    public sealed class EmbedQueryOutput
    {
        [OutputSocket(Description = "The embedding vector for the query.")]
        public required float[] Embedding { get; init; }
    }
}
