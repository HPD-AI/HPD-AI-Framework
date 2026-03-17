// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Agent;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Extension methods for AgentBuilder to register evaluators, score stores, and judge configs.
/// All evaluators added to the same builder share one EvaluationMiddleware instance.
/// </summary>
public static class AgentBuilderEvalExtensions
{
    /// <summary>
    /// Registers an evaluator with the agent. EvaluationMiddleware fires after each
    /// completed message turn, running all registered evaluators as fire-and-forget tasks.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="evaluator">The evaluator to register.</param>
    /// <param name="samplingRate">
    /// Fraction of turns to evaluate (0.0–1.0). Default 1.0 = every turn.
    /// </param>
    /// <param name="policy">
    /// MustAlwaysPass: failure emits EvalPolicyViolationEvent (CI gate).
    /// TrackTrend: failures are recorded in IScoreStore only (quality monitoring).
    /// </param>
    /// <param name="judgeConfig">
    /// Per-evaluator judge LLM override. Falls back to the global judge config
    /// set via UseEvalJudgeConfig, then to the agent's own provider.
    /// </param>
    public static AgentBuilder AddEvaluator(
        this AgentBuilder builder,
        IEvaluator evaluator,
        double samplingRate = 1.0,
        EvalPolicy policy = EvalPolicy.MustAlwaysPass,
        EvalJudgeConfig? judgeConfig = null)
    {
        var middleware = GetOrCreateMiddleware(builder);
        middleware.AddEvaluator(evaluator, samplingRate, policy, judgeConfig);
        return builder;
    }

    /// <summary>
    /// Sets the IScoreStore that EvaluationMiddleware writes results to after each turn.
    /// If not called, scores are emitted as EvalScoreEvents but not persisted.
    /// </summary>
    public static AgentBuilder UseScoreStore(this AgentBuilder builder, IScoreStore store)
    {
        var middleware = GetOrCreateMiddleware(builder);
        middleware.ScoreStore = store;
        return builder;
    }

    /// <summary>
    /// Sets the global judge LLM configuration used by all LLM-as-judge evaluators
    /// that do not have a per-evaluator judgeConfig override.
    /// </summary>
    public static AgentBuilder UseEvalJudgeConfig(this AgentBuilder builder, EvalJudgeConfig config)
    {
        var middleware = GetOrCreateMiddleware(builder);
        middleware.GlobalJudgeConfig = config;
        return builder;
    }

    /// <summary>
    /// Attaches an <see cref="AnnotationQueue"/> to the evaluation pipeline.
    /// After each turn where an evaluator produces a score below
    /// <see cref="AnnotationQueueOptions.AutoQueueBelowScore"/>, the turn is
    /// automatically enqueued for human review and an
    /// <see cref="EvalEvents.AnnotationRequestedEvent"/> is emitted.
    ///
    /// The caller retains a reference to <paramref name="queue"/> for claiming
    /// and completing annotations via <see cref="AnnotationQueue.ClaimNext"/>.
    /// </summary>
    public static AgentBuilder AddAnnotationQueue(
        this AgentBuilder builder,
        AnnotationQueue queue)
    {
        var middleware = GetOrCreateMiddleware(builder);
        middleware.AnnotationQueue = queue;
        return builder;
    }

    /// <summary>
    /// Creates and attaches a new <see cref="AnnotationQueue"/> with the given options.
    /// Returns the created queue so the caller can use it for claiming/completing annotations.
    /// </summary>
    public static AgentBuilder AddAnnotationQueue(
        this AgentBuilder builder,
        AnnotationQueueOptions options,
        out AnnotationQueue queue)
    {
        queue = new AnnotationQueue(options);
        return builder.AddAnnotationQueue(queue);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the shared EvaluationMiddleware on this builder, creating and registering
    /// it (as both middleware and observer) if it doesn't exist yet.
    /// </summary>
    private static EvaluationMiddleware GetOrCreateMiddleware(AgentBuilder builder)
    {
        var middleware = builder.Middlewares
            .OfType<EvaluationMiddleware>()
            .FirstOrDefault();

        if (middleware is null)
        {
            middleware = new EvaluationMiddleware();
            builder.Middlewares.Add(middleware);
            // Also register as an observer so OnEventAsync receives timing and permission events.
            // EvaluationMiddleware implements both IAgentMiddleware and IAgentEventObserver.
            builder.WithObserver(middleware);
        }

        return middleware;
    }
}
