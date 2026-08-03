// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Diagnostics.CodeAnalysis;
using System.Text;
using HPD.Agent;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI;
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
        EvalJudgeConfig? judgeConfig = null)
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
    /// Sets the global judge agent used by LLM-as-judge evaluators that do not
    /// have a per-evaluator judge override. The judge agent should normally be
    /// built with no tools and MaxAgenticIterations = 1.
    /// </summary>
    public static AgentBuilder UseEvalJudgeAgent(this AgentBuilder builder, IJudgeAgent judgeAgent)
        => builder.UseEvalJudgeConfig(new EvalJudgeConfig { OverrideAgent = judgeAgent });

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
    /// Builds and registers a dedicated judge agent for LLM-as-judge evaluators.
    /// The judge agent defaults to a single function-calling turn and no toolharnesses;
    /// callers provide the chat client/provider and any required safety middleware.
    /// </summary>
    [RequiresUnreferencedCode("Agent building may use ToolHarness registration methods that require reflection.")]
    public static async Task<AgentBuilder> UseEvalJudgeAgentAsync(
        this AgentBuilder builder,
        Action<AgentBuilder> configureJudge,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureJudge);

        var judgeBuilder = new AgentBuilder()
            .WithMaxFunctionCallTurns(1);

        configureJudge(judgeBuilder);
        judgeBuilder.WithEvalJudgeTraceCapture();

        var judgeAgent = await judgeBuilder.BuildAsync(cancellationToken)
            .ConfigureAwait(false);

        return builder.UseEvalJudgeAgent(new BuiltAgentJudgeAdapter(judgeAgent));
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

    private sealed class BuiltAgentJudgeAdapter(Agent judgeAgent) : IJudgeAgent
    {
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task<Microsoft.Extensions.AI.ChatResponse> RunAsync(
            AgentRunConfig config,
            CancellationToken ct = default)
        {
            var userMessage = config.UserMessage
                ?? throw new InvalidOperationException("Judge agent calls require AgentRunConfig.UserMessage.");

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var text = new StringBuilder();
                UsageDetails? usage = null;
                var finishedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                using var subscription = judgeAgent.SubscribeAny(evt =>
                {
                    switch (evt)
                    {
                        case TextDeltaEvent delta:
                            text.Append(delta.Text);
                            break;
                        case MessageTurnFinishedEvent finished:
                            usage = finished.Usage;
                            finishedSignal.TrySetResult();
                            break;
                    }
                });

                config.DisableEvaluators = true;
                config.IsInternalEvalJudgeCall = true;
                config.Tools ??= new AgentToolsRunConfig();
                config.Tools.Mode = ChatToolMode.None;

                await judgeAgent.RunAsync(new UserMessagesInputEvent { Messages = [
                    new ChatMessage(ChatRole.User, userMessage)
                ],
                    RunConfig = config,
                }, ct).ConfigureAwait(false);

                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                waitCts.CancelAfter(TimeSpan.FromSeconds(30));
                await finishedSignal.Task.WaitAsync(waitCts.Token).ConfigureAwait(false);

                return new Microsoft.Extensions.AI.ChatResponse(
                    [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, text.ToString())])
                {
                    Usage = usage,
                };
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
