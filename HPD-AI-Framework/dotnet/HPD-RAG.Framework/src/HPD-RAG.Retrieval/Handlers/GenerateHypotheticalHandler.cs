using HPDAgent.Graph.Abstractions.Attributes;
using HPDAgent.Graph.Abstractions.Handlers;
using HPD.RAG.Core.Context;
using HPD.RAG.Core.Pipeline;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Retrieval.Handlers;

/// <summary>
/// Generates a hypothetical document passage for HyDE (Hypothetical Document Embeddings).
/// The LLM is asked to produce an idealized answer; that text is embedded and used as the search vector.
/// Default retry: 3 attempts, JitteredExponential, 2–60s.
/// Default propagation: SkipDependents — pipeline continues without hypothetical on LLM failure.
/// </summary>
[GraphNodeHandler(NodeName = "GenerateHypothetical")]
public sealed partial class GenerateHypotheticalHandler : IGraphNodeHandler<MragPipelineContext>
{
    public static MragRetryPolicy DefaultRetryPolicy { get; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(2),
        Strategy = MragBackoffStrategy.JitteredExponential,
        MaxDelay = TimeSpan.FromSeconds(60)
    };

    public static MragErrorPropagation DefaultErrorPropagation { get; } = MragErrorPropagation.SkipDependents;

    private const string SystemPrompt =
        "You are a precise document generator. Given a question, write a short, factual passage " +
        "that directly answers it, as if it were an excerpt from an authoritative document. " +
        "Output only the passage text — no preamble, no explanation, no metadata.";

    public async Task<GenerateHypotheticalOutput> ExecuteAsync(
        MragPipelineContext context,
        [InputSocket(Description = "The query for which to generate a hypothetical passage.")] string Query,
        CancellationToken cancellationToken = default)
    {
        var chatClient = context.Services.GetRequiredKeyedService<IChatClient>("mrag:hypothetical");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, Query)
        };

        var response = await chatClient
            .GetResponseAsync(messages, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new GenerateHypotheticalOutput { Hypothetical = response.Text ?? string.Empty };
    }

    public sealed class GenerateHypotheticalOutput
    {
        [OutputSocket(Description = "A hypothetical passage that ideally answers the query.")]
        public required string Hypothetical { get; init; }
    }
}
