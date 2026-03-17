using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HPD.RAG.Evaluation.Handlers;

/// <summary>
/// Persists an evaluation metrics snapshot via <see cref="IEvaluationResultStore"/>.
/// When no store is registered in DI the metrics are logged at Information level
/// and <c>Stored</c> is set to <see langword="false"/>.
/// </summary>
[GraphNodeHandler(NodeName = "WriteEvalResult")]
public sealed partial class WriteEvalResultHandler : HPDAgent.Graph.Abstractions.Handlers.IGraphNodeHandler<HPD.RAG.Core.Context.MragPipelineContext>
{
    /// <summary>Default error propagation: stop the pipeline on persistence failure.</summary>
    public static Core.Pipeline.MragErrorPropagation DefaultPropagation { get; } =
        Core.Pipeline.MragErrorPropagation.StopPipeline;

    public async Task<Output> ExecuteAsync(
        [InputSocket(Description = "Evaluation scenario name")]
        string Scenario,
        [InputSocket(Description = "Iteration name within the scenario")]
        string Iteration,
        [InputSocket(Description = "Execution identifier (run ID, timestamp, etc.)")]
        string Execution,
        [InputSocket(Description = "Aggregated evaluation metrics to persist")]
        MragMetricsDto Metrics,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var store = context.Services.GetService<IEvaluationResultStore>();

        if (store is null)
        {
            var logger = context.Services.GetService<ILogger<WriteEvalResultHandler>>()
                         ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WriteEvalResultHandler>.Instance;

            logger.LogInformation(
                "No IEvaluationResultStore registered. Eval result for scenario={Scenario} " +
                "iteration={Iteration} execution={Execution}: Scores={Scores}",
                Scenario, Iteration, Execution,
                string.Join(", ", Metrics.Scores.Select(kvp => $"{kvp.Key}={kvp.Value:F4}")));

            return new Output { Stored = false };
        }

        await store.StoreAsync(Scenario, Iteration, Execution, Metrics, cancellationToken)
            .ConfigureAwait(false);

        return new Output { Stored = true };
    }

    public sealed record Output
    {
        [OutputSocket(Description = "True if the metrics were persisted; false when logging-only fallback was used")]
        public bool Stored { get; init; }
    }
}
