using HPD.RAG.Core.DTOs;

namespace HPD.RAG.Evaluation;

/// <summary>
/// Stores evaluation results produced by the evaluation handler catalog.
/// Register an implementation in DI to persist results; if not registered,
/// <see cref="Handlers.WriteEvalResultHandler"/> falls back to logging.
/// </summary>
public interface IEvaluationResultStore
{
    /// <summary>
    /// Persists a metrics snapshot for the given scenario/iteration/execution triple.
    /// </summary>
    Task StoreAsync(
        string scenario,
        string iteration,
        string execution,
        MragMetricsDto metrics,
        CancellationToken cancellationToken = default);
}
