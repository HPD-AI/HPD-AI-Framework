// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Middleware;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Evaluators;
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
/// Implements both IAgentMiddleware (for before/after turn hooks) and
/// IAgentEventObserver (for buffering timing and permission events).
///
/// Flow:
///   BeforeMessageTurnAsync → activate EvalContext, reset TurnEventBuffer
///   OnEventAsync          → populate buffer (timestamps, permission denials)
///   AfterMessageTurnAsync → build TurnEvaluationContext, launch evaluators fire-and-forget
/// </summary>
public sealed class EvaluationMiddleware : IAgentMiddleware, IAgentEventObserver
{
    private readonly List<EvaluatorRegistration> _evaluators = new();
    private readonly Random _rng = new();

    // Turn-scoped event buffer (one per active turn, AsyncLocal for thread safety)
    private readonly AsyncLocal<TurnEventBuffer?> _buffer = new();

    // Turn-scoped eval context data — captured at turn start so AfterMessageTurnAsync can read it
    private readonly AsyncLocal<EvalContextData?> _evalData = new();

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

        // Activate EvalContext for the duration of this turn and capture the data object
        // so AfterMessageTurnAsync can read accumulated attributes/metrics before deactivating.
        var evalData = EvalContext.Activate();
        _evalData.Value = evalData;

        // Start a fresh event buffer for this turn
        var buffer = new TurnEventBuffer();
        _buffer.Value = buffer;

        return Task.CompletedTask;
    }

    public async Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (context.RunConfig.IsInternalEvalJudgeCall || context.RunConfig.DisableEvaluators)
            return;

        if (_evaluators.Count == 0)
            return;

        var buffer = _buffer.Value;
        if (buffer is null)
            return;

        // Capture accumulated EvalContext data before deactivating.
        // _evalData was set in BeforeMessageTurnAsync; the AsyncLocal reference is still live.
        var evalData = _evalData.Value ?? new EvalContextData();
        EvalContext.Deactivate();
        _evalData.Value = null;

        // Build TurnEvaluationContext
        string? groundTruth = context.RunConfig.ContextOverrides?.TryGetValue("groundTruth", out var gt) == true
            ? gt?.ToString()
            : null;

        TurnEvaluationContext turnCtx;
        try
        {
            turnCtx = TurnEvaluationContextBuilder.FromAfterMessageTurn(context, buffer, evalData, groundTruth);
        }
        catch
        {
            return; // Don't crash the agent if context building fails
        }

        // Launch all evaluators as fire-and-forget background tasks
        // so they don't block AfterMessageTurnAsync from returning
        foreach (var registration in _evaluators)
        {
            // Sampling check
            if (registration.SamplingRate < 1.0 && _rng.NextDouble() > registration.SamplingRate)
                continue;

            var reg = registration;
            var ctx = context;
            var tCtx = turnCtx;

            _ = Task.Run(async () =>
            {
                await RunEvaluatorAsync(reg, tCtx, ctx, CancellationToken.None)
                    .ConfigureAwait(false);
            }, CancellationToken.None);
        }
    }

    // ── IAgentEventObserver ───────────────────────────────────────────────────

    public Task OnEventAsync(AgentEvent evt, CancellationToken ct = default)
    {
        var buffer = _buffer.Value;
        if (buffer is null)
            return Task.CompletedTask;

        switch (evt)
        {
            case MessageTurnStartedEvent e:
                buffer.RecordTurnStarted(e.MessageTurnId, e.Timestamp);
                break;

            case MessageTurnFinishedEvent e:
                buffer.RecordTurnFinished(e.Duration);
                break;

            case AgentTurnStartedEvent e:
                buffer.RecordIterationStarted(e.Iteration, e.Timestamp);
                break;

            case AgentTurnFinishedEvent e:
                buffer.RecordIterationFinished(e.Iteration, e.Timestamp);
                break;

            case ToolCallStartEvent e:
                buffer.RecordToolCallStarted(e.CallId, e.Name, e.ToolkitName, e.Timestamp);
                break;

            case ToolCallEndEvent e:
                buffer.RecordToolCallEnded(e.CallId, e.Timestamp);
                break;

            case PermissionDeniedEvent e:
                buffer.RecordPermissionDenied(e.CallId);
                break;
        }

        return Task.CompletedTask;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RunEvaluatorAsync(
        EvaluatorRegistration registration,
        TurnEvaluationContext turnCtx,
        AfterMessageTurnContext hookCtx,
        CancellationToken ct)
    {
        var evaluatorName = registration.Evaluator.GetType().Name;
        var version = (registration.Evaluator as IHpdEvaluator)?.Version ?? "1.0.0";

        // Build additional context including TurnEvaluationContextWrapper
        var additionalContext = new List<EvaluationContext>
        {
            new TurnEvaluationContextWrapper(turnCtx),
        };

        // Resolve judge ChatConfiguration if needed
        ChatConfiguration? chatConfig = null;
        if (registration.Evaluator is not HpdDeterministicEvaluatorBase &&
            registration.Evaluator is not TaskOracleEvaluator)
        {
            var judgeConfig = registration.JudgeConfig ?? GlobalJudgeConfig;
            if (judgeConfig?.OverrideChatClient is not null)
                chatConfig = new ChatConfiguration(judgeConfig.OverrideChatClient);
        }

        // Build timeout CTS
        var judgeConfig2 = registration.JudgeConfig ?? GlobalJudgeConfig;
        int timeoutSeconds = judgeConfig2?.TimeoutSeconds ?? 30;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        EvaluationResult result;
        bool timedOut = false;
        string? errorMessage = null;

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
            timedOut = true;
            errorMessage = $"Evaluator '{evaluatorName}' timed out after {timeoutSeconds}s.";

            hookCtx.Emit(new EvalFailedEvent
            {
                EvaluatorName = evaluatorName,
                SessionId = turnCtx.SessionId,
                BranchId = turnCtx.BranchId,
                TurnIndex = turnCtx.TurnIndex,
                ErrorMessage = errorMessage,
                TimedOut = true,
            });
            return;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            hookCtx.Emit(new EvalFailedEvent
            {
                EvaluatorName = evaluatorName,
                SessionId = turnCtx.SessionId,
                BranchId = turnCtx.BranchId,
                TurnIndex = turnCtx.TurnIndex,
                ErrorMessage = errorMessage,
                TimedOut = false,
                Exception = ex,
            });
            return;
        }

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
                BranchId = turnCtx.BranchId,
                TurnIndex = turnCtx.TurnIndex,
                AgentName = turnCtx.AgentName,
                ModelId = turnCtx.ModelId,
                TurnUsage = turnCtx.TurnUsage,
                TurnDuration = turnCtx.Duration,
                Attributes = turnCtx.Attributes,
                Metrics = turnCtx.Metrics,
                SamplingRate = registration.SamplingRate,
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
            BranchId = turnCtx.BranchId,
            TurnIndex = turnCtx.TurnIndex,
            EvaluatorDuration = TimeSpan.Zero, // timing captured in ParseJudgeResponse for LLM judges
        });

        // Annotation queue: if a queue is configured and the score falls below the threshold,
        // enqueue the turn for human review and emit AnnotationRequestedEvent.
        if (AnnotationQueue is not null)
        {
            double? primaryScore = GetPrimaryScore(result);
            if (primaryScore.HasValue)
            {
                var annotationId = AnnotationQueue.TryEnqueueFromScore(
                    turnCtx.SessionId, turnCtx.BranchId, turnCtx.TurnIndex,
                    evaluatorName, primaryScore.Value);

                if (annotationId is not null)
                {
                    hookCtx.Emit(new AnnotationRequestedEvent
                    {
                        AnnotationId = annotationId,
                        SessionId = turnCtx.SessionId,
                        BranchId = turnCtx.BranchId,
                        TurnIndex = turnCtx.TurnIndex,
                        TriggerEvaluatorName = evaluatorName,
                        TriggerScore = primaryScore.Value,
                    });
                }
            }
        }

        // For MustAlwaysPass evaluators, check for failures and emit EvalPolicyViolationEvent
        if (registration.Policy == EvalPolicy.MustAlwaysPass)
        {
            foreach (var (metricName, metric) in result.Metrics)
            {
                bool failed = metric switch
                {
                    BooleanMetric bm => bm.Value == false,
                    NumericMetric nm => nm.Value.HasValue && nm.Value.Value <= 0,
                    _ => false,
                };

                if (failed)
                {
                    hookCtx.Emit(new EvalPolicyViolationEvent
                    {
                        EvaluatorName = evaluatorName,
                        MetricName = metricName,
                        SessionId = turnCtx.SessionId,
                        BranchId = turnCtx.BranchId,
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
}
