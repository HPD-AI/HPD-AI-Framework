// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Extension methods for AgentBuilder to register evaluators, score stores, and judge configs.
/// All evaluators added to the same builder share one LiveEvaluationMiddleware instance.
/// </summary>
public static class AgentBuilderEvalExtensions
{
    /// <summary>
    /// Registers an evaluator with the agent. LiveEvaluationMiddleware fires after each
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
    /// <param name="judgeConfig">Per-evaluator judge override.</param>
    public static AgentBuilder AddEvaluator(
        this AgentBuilder builder,
        IEvaluator evaluator,
        double samplingRate = 1.0,
        EvalPolicy policy = EvalPolicy.MustAlwaysPass,
        EvaluationJudgeRunConfig? judgeConfig = null)
    {
        var middleware = GetOrCreateMiddleware(builder);
        middleware.AddEvaluator(evaluator, samplingRate, policy, judgeConfig);
        return builder;
    }

    /// <summary>
    /// Sets the IScoreStore that LiveEvaluationMiddleware writes results to after each turn.
    /// If not called, scores are emitted as EvalScoreEvents but not persisted.
    /// </summary>
    public static AgentBuilder UseScoreStore(this AgentBuilder builder, IScoreStore store)
    {
        var middleware = GetOrCreateMiddleware(builder);
        middleware.ScoreStore = store;
        return builder;
    }

    /// <summary>
    /// Registers the middleware that captures the post-middleware judge model
    /// request for evaluation trace storage. Register it after privacy/prompt
    /// middleware so the captured prompt is the sanitized prompt.
    /// </summary>
    public static AgentBuilder WithEvalJudgeTraceCapture(this AgentBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!builder.Middlewares.OfType<EvalJudgeTraceCaptureMiddleware>().Any())
            builder.WithMiddleware(new EvalJudgeTraceCaptureMiddleware());

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
    /// Finds the shared LiveEvaluationMiddleware on this builder, creating and registering
    /// it (as middleware and event subscription) if it doesn't exist yet.
    /// </summary>
    private static LiveEvaluationMiddleware GetOrCreateMiddleware(AgentBuilder builder)
    {
        var middleware = builder.Middlewares
            .OfType<LiveEvaluationMiddleware>()
            .FirstOrDefault();

        if (middleware is null)
        {
            middleware = new LiveEvaluationMiddleware();
            builder.Middlewares.Insert(0, middleware);
            builder.WithEventSubscription(coordinator =>
                coordinator.Subscribe<AgentEvent>(middleware.HandleAsync));
        }
        else
        {
            PinLiveEvaluationMiddlewareOutermost(builder, middleware);
        }

        return middleware;
    }

    private static void PinLiveEvaluationMiddlewareOutermost(
        AgentBuilder builder,
        LiveEvaluationMiddleware middleware)
    {
        var index = builder.Middlewares.IndexOf(middleware);
        if (index <= 0)
            return;

        builder.Middlewares.RemoveAt(index);
        builder.Middlewares.Insert(0, middleware);
    }

}
