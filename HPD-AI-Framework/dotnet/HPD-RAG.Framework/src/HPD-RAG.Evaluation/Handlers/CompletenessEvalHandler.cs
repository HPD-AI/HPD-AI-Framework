using HPD.RAG.Core.Context;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.RAG.Evaluation.Handlers;

/// <summary>
/// Evaluates the completeness of a model response against a ground-truth reference.
/// Scores range from 1 (poor) to 5 (excellent) as defined by
/// <see cref="CompletenessEvaluator"/>.
/// </summary>
[GraphNodeHandler(NodeName = "EvalCompleteness")]
public sealed partial class CompletenessEvalHandler : HPDAgent.Graph.Abstractions.Handlers.IGraphNodeHandler<HPD.RAG.Core.Context.MragPipelineContext>
{
    private static readonly CompletenessEvaluator _evaluator = new();

    /// <summary>Default error propagation: isolate so downstream eval nodes still run.</summary>
    public static Core.Pipeline.MragErrorPropagation DefaultPropagation { get; } =
        Core.Pipeline.MragErrorPropagation.Isolate;

    public async Task<Output> ExecuteAsync(
        [InputSocket(Description = "Conversation history that produced the response")]
        ChatMessage[] Messages,
        [InputSocket(Description = "The model response to evaluate")]
        ChatResponse Response,
        [InputSocket(Description = "The ground truth the response is measured against")]
        string GroundTruth,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var chatClient = context.Services.GetRequiredKeyedService<IChatClient>("mrag:judge");
        var chatConfig = new ChatConfiguration(chatClient);

        var additionalContext = new EvaluationContext[]
        {
            new CompletenessEvaluatorContext(GroundTruth)
        };

        EvaluationResult result = await _evaluator.EvaluateAsync(
            messages: Messages,
            modelResponse: Response,
            chatConfiguration: chatConfig,
            additionalContext: additionalContext,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var metric = result.Metrics.Values.FirstOrDefault();
        double score = metric is NumericMetric nm && nm.Value.HasValue ? nm.Value.Value : 0.0;
        string reason = metric?.Reason ?? string.Empty;

        return new Output { Score = score, Reason = reason };
    }

    public sealed record Output
    {
        [OutputSocket(Description = "Completeness score in [1, 5]")]
        public double Score { get; init; }

        [OutputSocket(Description = "Human-readable explanation of the score")]
        public string Reason { get; init; } = string.Empty;
    }
}
