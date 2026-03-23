using HPD.RAG.Core.Context;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Evaluation.Handlers;

/// <summary>
/// Evaluates the linguistic fluency of a model response using an AI judge.
/// Scores range from 1 (poor) to 5 (excellent) as defined by
/// <see cref="FluencyEvaluator"/>.
/// </summary>
[GraphNodeHandler(NodeName = "EvalFluency")]
public sealed partial class FluencyEvalHandler : HPDAgent.Graph.Abstractions.Handlers.IGraphNodeHandler<HPD.RAG.Core.Context.MragPipelineContext>
{
    private static readonly FluencyEvaluator _evaluator = new();

    /// <summary>Default error propagation: isolate so downstream eval nodes still run.</summary>
    public static Core.Pipeline.MragErrorPropagation DefaultPropagation { get; } =
        Core.Pipeline.MragErrorPropagation.Isolate;

    public async Task<Output> ExecuteAsync(
        [InputSocket(Description = "Conversation history that produced the response")]
        ChatMessage[] Messages,
        [InputSocket(Description = "The model response to evaluate")]
        ChatResponse Response,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var chatClient = context.Services.GetRequiredKeyedService<IChatClient>("mrag:judge");
        var chatConfig = new ChatConfiguration(chatClient);

        EvaluationResult result = await _evaluator.EvaluateAsync(
            messages: Messages,
            modelResponse: Response,
            chatConfiguration: chatConfig,
            additionalContext: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var metric = result.Metrics.Values.FirstOrDefault();
        double score = metric is NumericMetric nm && nm.Value.HasValue ? nm.Value.Value : 0.0;
        string reason = metric?.Reason ?? string.Empty;

        return new Output { Score = score, Reason = reason };
    }

    public sealed record Output
    {
        [OutputSocket(Description = "Fluency score in [1, 5]")]
        public double Score { get; init; }

        [OutputSocket(Description = "Human-readable explanation of the score")]
        public string Reason { get; init; } = string.Empty;
    }
}
