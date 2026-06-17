// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Middleware;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;
using HPD.Agent.Evaluations.Storage;
using HPD.Agent.Evaluations.Tracing;
using HPD.Agent.Evaluations.Integration;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Registration entry for a single evaluator with its config.
/// </summary>
internal sealed record EvaluatorRegistration(
    IEvaluator Evaluator,
    double SamplingRate,
    EvalPolicy Policy,
    EvalJudgeConfig? JudgeConfig);

/// <summary>
/// Core middleware that wires the HPD evaluation system into the agent lifecycle.
/// Implements middleware hooks and subscribes to agent events for buffering timing and permission events.
///
/// Flow:
///   BeforeMessageTurnAsync → activate EvalContext, reset TurnEventBuffer
///   HandleAsync           → populate buffer (timestamps, permission denials)
///   AfterMessageTurnAsync → build TurnEvaluationContext, launch evaluators fire-and-forget
/// </summary>
public sealed class LiveEvaluationMiddleware : IAgentMiddleware
{
    private const int MaxStoredConversationResponseLength = 4000;

    private readonly List<EvaluatorRegistration> _evaluators = new();
    private readonly Random _rng = new();
    private readonly EvalTurnCapture _capture = new();

    public IScoreStore? ScoreStore { get; set; }
    public EvalJudgeConfig? GlobalJudgeConfig { get; set; }

    /// <summary>
    /// Optional annotation queue. When set, turns whose evaluator score falls below
    /// <see cref="AnnotationQueueOptions.AutoQueueBelowScore"/> are automatically
    /// enqueued and an <see cref="AnnotationRequestedEvent"/> is emitted.
    /// </summary>
    public AnnotationQueue? AnnotationQueue { get; set; }

    internal void AddEvaluator(IEvaluator evaluator, double samplingRate, EvalPolicy policy, EvalJudgeConfig? judgeConfig)
        => _evaluators.Add(new EvaluatorRegistration(evaluator, samplingRate, policy, judgeConfig));

    // ── IAgentMiddleware ──────────────────────────────────────────────────────

    public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        // Don't activate if this is an internal judge call or evaluators are disabled
        if (context.RunConfig.IsInternalEvalJudgeCall || context.RunConfig.DisableEvaluators)
            return Task.CompletedTask;

        if (BuildRegistrations(context.RunConfig).Count == 0)
            return Task.CompletedTask;

        _capture.Begin(context);

        return Task.CompletedTask;
    }

    public async Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (context.RunConfig.IsInternalEvalJudgeCall || context.RunConfig.DisableEvaluators)
            return;

        var registrations = BuildRegistrations(context.RunConfig);
        if (registrations.Count == 0)
            return;

        var turnCtx = await _capture.CompleteAsync(context, cancellationToken).ConfigureAwait(false);
        if (turnCtx is null)
            return; // Don't crash the agent if context building fails

        var conversationEvalState = context.GetMiddlewareState<ConversationEvalStateData>()
            ?? new ConversationEvalStateData();
        var conversationHistory = BuildConversationHistoryForEvaluation(turnCtx, conversationEvalState);

        try
        {
            context.UpdateMiddlewareState<ConversationEvalStateData>(state =>
                AdvanceConversationEvalState(state, turnCtx));
        }
        catch
        {
            // Conversation state is for evaluator quality only; never fail the agent turn.
        }

        // Launch all evaluators as fire-and-forget background tasks
        // so they don't block AfterMessageTurnAsync from returning
        var samplingOverride = context.RunConfig.EvaluatorSamplingOverride;
        foreach (var registration in registrations)
        {
            // Sampling check
            var samplingRate = samplingOverride ?? registration.SamplingRate;
            if (samplingRate < 1.0 && _rng.NextDouble() > samplingRate)
                continue;

            var reg = registration;
            var ctx = context;
            var tCtx = turnCtx;

            _ = Task.Run(async () =>
            {
                await RunEvaluatorAsync(reg, tCtx, conversationHistory, ctx, samplingRate, CancellationToken.None)
                    .ConfigureAwait(false);
            }, CancellationToken.None);
        }
    }

    // ── Event subscription ────────────────────────────────────────────────────

    public ValueTask HandleAsync(AgentEvent evt)
        => _capture.HandleAsync(evt);

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RunEvaluatorAsync(
        EvaluatorRegistration registration,
        TurnEvaluationContext turnCtx,
        IReadOnlyList<ChatMessage> conversationHistory,
        AfterMessageTurnContext hookCtx,
        double effectiveSamplingRate,
        CancellationToken ct)
    {
        var evaluatorName = registration.Evaluator.GetType().Name;
        var version = EvaluationExecutionHelpers.ResolveEvaluatorVersion(registration.Evaluator);

        // Build additional context including TurnEvaluationContextWrapper
        var additionalContext = new List<EvaluationContext>
        {
            new TurnEvaluationContextWrapper(turnCtx),
        };
        if (conversationHistory.Count > 0)
        {
            additionalContext.Add(new ConversationHistoryContext(conversationHistory));
        }

        // Resolve judge ChatConfiguration if needed
        ChatConfiguration? chatConfig = null;
        if (registration.Evaluator is not HpdDeterministicEvaluatorBase &&
            registration.Evaluator is not TaskOracleEvaluator)
        {
            var judgeConfig = registration.JudgeConfig ??
                hookCtx.RunConfig.GetEvalJudgeConfigOverride() ??
                GlobalJudgeConfig;
            chatConfig = EvaluationExecutionHelpers.BuildChatConfiguration(judgeConfig);
        }

        // Build timeout CTS
        var judgeConfig2 = registration.JudgeConfig ??
            hookCtx.RunConfig.GetEvalJudgeConfigOverride() ??
            GlobalJudgeConfig;
        int timeoutSeconds = judgeConfig2?.TimeoutSeconds ?? 30;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        EvaluationResult result;
        var evaluatorStart = DateTimeOffset.UtcNow;
        using var traceScope = EvalTraceContext.Activate(evaluatorName);

        try
        {
            result = await registration.Evaluator.EvaluateAsync(
                messages: hookCtx.TurnHistory,
                modelResponse: hookCtx.FinalResponse,
                chatConfiguration: chatConfig,
                additionalContext: additionalContext,
                cancellationToken: cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            var errorMessage = $"Evaluator '{evaluatorName}' timed out after {timeoutSeconds}s.";

            hookCtx.Emit(new EvalFailedEvent
            {
                EvaluatorName = evaluatorName,
                SessionId = turnCtx.SessionId,
                ThreadId = turnCtx.ThreadId,
                TurnIndex = turnCtx.TurnIndex,
                ErrorMessage = errorMessage,
                TimedOut = true,
            });
            return;
        }
        catch (Exception ex)
        {
            var errorMessage = ex.Message;
            hookCtx.Emit(new EvalFailedEvent
            {
                EvaluatorName = evaluatorName,
                SessionId = turnCtx.SessionId,
                ThreadId = turnCtx.ThreadId,
                TurnIndex = turnCtx.TurnIndex,
                ErrorMessage = errorMessage,
                TimedOut = false,
                Exception = ex,
            });
            return;
        }

        var evaluatorDuration = DateTimeOffset.UtcNow - evaluatorStart;
        var judgeCalls = traceScope.Snapshot();
        var (judgeModelId, judgeUsage, judgeDuration) =
            EvaluationExecutionHelpers.ExtractJudgeMetadata(result);

        // Persist to IScoreStore
        if (ScoreStore is not null)
        {
            var record = new ScoreRecord
            {
                Id = Guid.NewGuid().ToString(),
                EvaluatorName = evaluatorName,
                EvaluatorVersion = version,
                Result = result,
                Source = EvaluationSource.Live,
                SessionId = turnCtx.SessionId,
                ThreadId = turnCtx.ThreadId,
                TurnIndex = turnCtx.TurnIndex,
                AgentName = turnCtx.AgentName,
                ProviderKey = turnCtx.ProviderKey,
                ModelId = turnCtx.ModelId,
                ResponseModelId = turnCtx.ResponseModelId,
                TurnUsage = turnCtx.TurnUsage,
                TurnDuration = turnCtx.Duration,
                Attributes = turnCtx.Attributes,
                Metrics = turnCtx.Metrics,
                JudgeModelId = judgeModelId,
                JudgeUsage = judgeUsage,
                JudgeDuration = judgeDuration ?? evaluatorDuration,
                JudgeCalls = judgeCalls,
                SamplingRate = effectiveSamplingRate,
                Policy = registration.Policy,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            try
            {
                await ScoreStore.WriteScoreAsync(record, ct).ConfigureAwait(false);
            }
            catch { /* store write failure is non-fatal */ }
        }

        // Emit EvalScoreEvent
        hookCtx.Emit(new EvalScoreEvent
        {
            EvaluatorName = evaluatorName,
            EvaluatorVersion = version,
            Result = result,
            Source = EvaluationSource.Live,
            SessionId = turnCtx.SessionId,
            ThreadId = turnCtx.ThreadId,
            TurnIndex = turnCtx.TurnIndex,
            EvaluatorDuration = evaluatorDuration,
        });

        // Annotation queue: if a queue is configured and the score falls below the threshold,
        // enqueue the turn for human review and emit AnnotationRequestedEvent.
        if (AnnotationQueue is not null)
        {
            double? primaryScore = GetPrimaryScore(result);
            if (primaryScore.HasValue)
            {
                var annotationId = AnnotationQueue.TryEnqueueFromScore(
                    turnCtx.SessionId, turnCtx.ThreadId, turnCtx.TurnIndex,
                    evaluatorName, primaryScore.Value);

                if (annotationId is not null)
                {
                    _ = WaitForAnnotationResponseAsync(
                        annotationId,
                        evaluatorName,
                        version,
                        turnCtx,
                        new AnnotationRequestedEvent
                        {
                            AnnotationId = annotationId,
                            SessionId = turnCtx.SessionId,
                            ThreadId = turnCtx.ThreadId,
                            TurnIndex = turnCtx.TurnIndex,
                            TriggerEvaluatorName = evaluatorName,
                            TriggerScore = primaryScore.Value,
                        },
                        hookCtx,
                        CancellationToken.None);
                }
            }
        }

        // For MustAlwaysPass evaluators, check for failures and emit EvalPolicyViolationEvent
        if (registration.Policy == EvalPolicy.MustAlwaysPass)
        {
            foreach (var (metricName, metric) in result.Metrics)
            {
                bool failed = EvaluationExecutionHelpers.IsFailingMetric(metric);

                if (failed)
                {
                    hookCtx.Emit(new EvalPolicyViolationEvent
                    {
                        EvaluatorName = evaluatorName,
                        MetricName = metricName,
                        SessionId = turnCtx.SessionId,
                        ThreadId = turnCtx.ThreadId,
                        TurnIndex = turnCtx.TurnIndex,
                        Result = result,
                    });
                }
            }
        }
    }

    private static double? GetPrimaryScore(EvaluationResult result)
    {
        var first = result.Metrics.FirstOrDefault();
        return first.Value switch
        {
            NumericMetric nm => nm.Value,
            BooleanMetric bm => bm.Value.HasValue ? (bm.Value.Value ? 1.0 : 0.0) : null,
            _ => null,
        };
    }

    private IReadOnlyList<EvaluatorRegistration> BuildRegistrations(AgentRunConfig runConfig)
    {
        var registrations = new List<EvaluatorRegistration>(_evaluators);

        foreach (var evaluator in runConfig.GetAdditionalEvaluators())
        {
            registrations.Add(new EvaluatorRegistration(
                evaluator,
                SamplingRate: 1.0,
                Policy: ResolveDefaultPolicy(evaluator),
                JudgeConfig: null));
        }

        return registrations;
    }

    private static EvalPolicy ResolveDefaultPolicy(IEvaluator evaluator)
        => evaluator is HpdDeterministicEvaluatorBase
            ? EvalPolicy.MustAlwaysPass
            : EvalPolicy.TrackTrend;

    private async Task WaitForAnnotationResponseAsync(
        string annotationId,
        string triggerEvaluatorName,
        string triggerEvaluatorVersion,
        TurnEvaluationContext turnCtx,
        AnnotationRequestedEvent request,
        AfterMessageTurnContext hookCtx,
        CancellationToken ct)
    {
        if (AnnotationQueue is null)
            return;

        AnnotationResponseEvent response;
        try
        {
            response = await hookCtx.RequestAsync<AnnotationRequestedEvent, AnnotationResponseEvent>(
                request,
                AnnotationQueue.LockTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        AnnotationQueue.SubmitResponse(
            annotationId,
            response.ReviewerId,
            response.Label,
            response.Score,
            response.Comment);

        if (ScoreStore is null)
            return;

        var evaluatorName = string.IsNullOrWhiteSpace(response.EvaluatorName)
            ? triggerEvaluatorName
            : response.EvaluatorName!;
        var metricName = string.IsNullOrWhiteSpace(response.MetricName)
            ? evaluatorName
            : response.MetricName!;
        var result = BuildHumanAnnotationResult(annotationId, metricName, response);

        var record = new ScoreRecord
        {
            Id = Guid.NewGuid().ToString(),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = triggerEvaluatorVersion,
            Result = result,
            Source = EvaluationSource.Human,
            SessionId = turnCtx.SessionId,
            ThreadId = turnCtx.ThreadId,
            TurnIndex = turnCtx.TurnIndex,
            AgentName = turnCtx.AgentName,
            ProviderKey = turnCtx.ProviderKey,
            ModelId = turnCtx.ModelId,
            ResponseModelId = turnCtx.ResponseModelId,
            TurnUsage = turnCtx.TurnUsage,
            TurnDuration = turnCtx.Duration,
            Attributes = turnCtx.Attributes,
            Metrics = turnCtx.Metrics,
            SamplingRate = 1.0,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await ScoreStore.WriteScoreAsync(record, ct).ConfigureAwait(false);
        }
        catch
        {
            // Human annotation persistence should not affect the agent turn.
        }
    }

    internal static EvaluationResult BuildHumanAnnotationResult(
        string annotationId,
        string metricName,
        AnnotationResponseEvent response)
    {
        EvaluationMetric metric;
        if (response.Score.HasValue)
        {
            metric = new NumericMetric(metricName)
            {
                Value = response.Score.Value,
                Reason = response.Comment ?? response.Label,
            };
        }
        else if (bool.TryParse(response.Label, out var labelAsBool))
        {
            metric = new BooleanMetric(metricName)
            {
                Value = labelAsBool,
                Reason = response.Comment,
            };
        }
        else
        {
            metric = new StringMetric(metricName)
            {
                Value = response.Label,
                Reason = response.Comment,
            };
        }

        metric.AddOrUpdateMetadata("annotation-id", annotationId);
        metric.AddOrUpdateMetadata("reviewer-id", response.ReviewerId);
        metric.AddOrUpdateMetadata("human-label", response.Label);
        return new EvaluationResult(metric);
    }

    internal static IReadOnlyList<ChatMessage> BuildConversationHistoryForEvaluation(
        TurnEvaluationContext turnCtx,
        ConversationEvalStateData state)
    {
        var history = new List<ChatMessage>(turnCtx.ConversationHistory);

        foreach (var response in state.PriorResponses)
        {
            if (string.IsNullOrWhiteSpace(response))
                continue;

            bool alreadyPresent = history.Any(message =>
                message.Role == ChatRole.Assistant &&
                string.Equals(message.Text, response, StringComparison.Ordinal));

            if (!alreadyPresent)
            {
                history.Add(new ChatMessage(ChatRole.Assistant, response));
            }
        }

        return history;
    }

    internal static ConversationEvalStateData AdvanceConversationEvalState(
        ConversationEvalStateData state,
        TurnEvaluationContext turnCtx)
    {
        var priorResponses = state.PriorResponses.ToList();
        if (!string.IsNullOrWhiteSpace(turnCtx.OutputText))
        {
            priorResponses.Add(TrimForConversationState(turnCtx.OutputText));
        }

        return state with
        {
            EstablishedGoal = string.IsNullOrWhiteSpace(state.EstablishedGoal)
                ? turnCtx.UserInput
                : state.EstablishedGoal,
            PriorResponses = priorResponses,
            TurnCount = state.TurnCount + 1,
        };
    }

    private static string TrimForConversationState(string value)
        => value.Length <= MaxStoredConversationResponseLength
            ? value
            : value[..MaxStoredConversationResponseLength];
}
