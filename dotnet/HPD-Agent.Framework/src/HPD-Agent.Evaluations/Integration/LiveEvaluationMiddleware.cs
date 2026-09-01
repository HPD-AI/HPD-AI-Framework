// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

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
using HPD.Agent.Providers;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Registration entry for a single evaluator with its config.
/// </summary>
internal sealed record EvaluatorRegistration(
    IEvaluator Evaluator,
    double SamplingRate,
    EvalPolicy Policy,
    EvaluationJudgeRunConfig? JudgeConfig);

internal interface IEvaluationJudgeClientFactory
{
    IEvaluationJudgeClientSession Create(EvaluationJudgeRunConfig config);
}

internal interface IEvaluationJudgeClientSession : IAsyncDisposable
{
    IChatClient Client { get; }
}

internal sealed class EvaluationJudgeClientFactory(
    IProviderRegistry? providerRegistry,
    IServiceProvider? services,
    AgentConfig agentConfig,
    AgentRunConfig runConfig) : IEvaluationJudgeClientFactory
{
    public IEvaluationJudgeClientSession Create(EvaluationJudgeRunConfig config)
    {
        var resolver = new AgentChatClientResolver(providerRegistry, services);
        var inner = new AgentSpecializedChatClient(
            resolver,
            agentConfig,
            runConfig,
            resolvedPrimary: null,
            config.Chat,
            config.Inheritance);
        return new EvaluationJudgeClientSession(inner, resolver);
    }

    private sealed class EvaluationJudgeClientSession(
        IChatClient client,
        AgentChatClientResolver owner) : IEvaluationJudgeClientSession
    {
        public IChatClient Client { get; } = client;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal sealed record LiveEvaluationExecutionSnapshot(
    AgentRunConfig RunConfig,
    IEvaluationJudgeClientFactory JudgeClients,
    Func<AgentEvent, CancellationToken, ValueTask<AgentEvent>> PublishAsync,
    Func<AnnotationRequestedEvent, TimeSpan, Task<AnnotationResponseEvent>> RequestAnnotationAsync,
    ConversationEvalStateData ConversationState);

/// <summary>
/// Core middleware that wires the HPD evaluation system into the agent lifecycle.
/// Implements middleware hooks and subscribes to agent events for buffering timing and permission events.
///
/// Flow:
///   BeforeMessageTurnAsync → activate EvalContext, reset TurnEventBuffer
///   HandleAsync           → populate buffer (timestamps, permission denials)
///   AfterMessageTurnAsync → prepare an immutable turn capture without waiting
///   MessageTurnFinished  → complete the capture and schedule evaluators
/// </summary>
public sealed class LiveEvaluationMiddleware : IAgentMiddleware
{
    private const int MaxStoredConversationResponseLength = 4000;

    private readonly List<EvaluatorRegistration> _evaluators = new();
    private readonly Random _rng = new();
    private readonly EvalTurnCapture _capture = new();

    public IScoreStore? ScoreStore { get; set; }
    public EvaluationJudgeRunConfig? GlobalJudgeConfig { get; set; }

    /// <summary>
    /// Optional annotation queue. When set, turns whose evaluator score falls below
    /// <see cref="AnnotationQueueOptions.AutoQueueBelowScore"/> are automatically
    /// enqueued and an <see cref="AnnotationRequestedEvent"/> is emitted.
    /// </summary>
    public AnnotationQueue? AnnotationQueue { get; set; }

    internal void AddEvaluator(IEvaluator evaluator, double samplingRate, EvalPolicy policy, EvaluationJudgeRunConfig? judgeConfig)
        => _evaluators.Add(new EvaluatorRegistration(evaluator, samplingRate, policy, judgeConfig));

    // ── IAgentMiddleware ──────────────────────────────────────────────────────

    public Task BeforeMessageTurnAsync(BeforeMessageTurnContext context, CancellationToken cancellationToken)
    {
        // Don't activate if this is an internal judge call or evaluators are disabled
        if (context.RunConfig.IsEvaluationSuppressed())
            return Task.CompletedTask;

        if (BuildRegistrations(context.RunConfig).Count == 0)
            return Task.CompletedTask;

        _capture.Begin(context);

        return Task.CompletedTask;
    }

    public Task AfterMessageTurnAsync(AfterMessageTurnContext context, CancellationToken cancellationToken)
    {
        if (context.RunConfig.IsEvaluationSuppressed())
            return Task.CompletedTask;

        var registrations = BuildRegistrations(context.RunConfig);
        if (registrations.Count == 0)
            return Task.CompletedTask;

        var events = context.Base.EventCoordinator;
        var threadEvents = context.Base.ThreadEvents;
        var sessionId = context.SessionId;
        var threadId = context.ThreadId;
        var traceId = context.TraceId;
        var threadExecutionId = context.ThreadExecutionId;
        async ValueTask<AgentEvent> PublishAsync(AgentEvent evt, CancellationToken ct)
        {
            if (traceId is not null && evt.TraceId is null)
                evt = evt with { TraceId = traceId };
            if (threadExecutionId is not null && evt.ThreadExecutionId is null)
                evt = evt with { ThreadExecutionId = threadExecutionId };
            if (threadEvents is not null && sessionId is not null && threadId is not null)
                return await threadEvents.CommitAndPublishAsync(new ThreadKey(sessionId, threadId), evt, ct).ConfigureAwait(false);
            await events.EmitAsync(evt, ct).ConfigureAwait(false);
            return evt;
        }
        async Task<AnnotationResponseEvent> RequestAnnotationAsync(AnnotationRequestedEvent request, TimeSpan timeout)
        {
            var handle = events.RegisterRequest<AnnotationRequestedEvent, AnnotationResponseEvent>(
                request,
                new HPD.Events.RequestOptions { Timeout = timeout });
            await PublishAsync(request, CancellationToken.None).ConfigureAwait(false);
            return (AnnotationResponseEvent)await handle.Response.ConfigureAwait(false);
        }

        var resolver = context.Base.ChatClientResolver
            ?? throw new InvalidOperationException("Evaluation judge resolution requires the invocation Chat resolver.");
        var runConfigSnapshot = AgentRunConfigSnapshot.Capture(context.RunConfig, resolver.Composition)
            ?? throw new InvalidOperationException("Evaluation run configuration snapshot failed.");
        var agentConfigSnapshot = AgentConfigSnapshot.Create(
            context.Config ?? throw new InvalidOperationException("Evaluation judge resolution requires the agent configuration."));
        var runtime = new LiveEvaluationExecutionSnapshot(
            runConfigSnapshot,
            new EvaluationJudgeClientFactory(
                resolver.ProviderRegistry,
                context.Services,
                agentConfigSnapshot,
                runConfigSnapshot),
            PublishAsync,
            RequestAnnotationAsync,
            context.GetMiddlewareState<ConversationEvalStateData>() ?? new ConversationEvalStateData());
        try
        {
            _capture.Prepare(
                context,
                turnCtx => CompletePreparedTurn(runtime, registrations, turnCtx),
                failed: _ => { },
                prepared: turnCtx =>
                {
                    try
                    {
                        context.UpdateMiddlewareState<ConversationEvalStateData>(state =>
                            AdvanceConversationEvalState(state, turnCtx));
                    }
                    catch { }
                });
        }
        catch
        {
            // Optional evaluation capture must not fail the owning agent turn.
        }
        return Task.CompletedTask;
    }

    public Task AfterInputAsync(AfterInputContext context, CancellationToken cancellationToken)
    {
        if (context.Result.Finished is null)
        {
            var traceId = context.Result.Started?.TraceId ?? context.Result.Events.FirstOrDefault()?.TraceId;
            var messageTurnId = context.Result.Started?.MessageTurnId ??
                context.Result.Events.OfType<MessageTurnStartedEvent>().FirstOrDefault()?.MessageTurnId;
            _capture.Fail(traceId, messageTurnId, context.Error ?? new OperationCanceledException("Evaluation input did not complete."));
        }
        _capture.EndInputScope();
        return Task.CompletedTask;
    }

    private void CompletePreparedTurn(
        LiveEvaluationExecutionSnapshot runtime,
        IReadOnlyList<EvaluatorRegistration> registrations,
        TurnEvaluationContext turnCtx)
    {

        var conversationHistory = BuildConversationHistoryForEvaluation(turnCtx, runtime.ConversationState);

        // The committed terminal event has now completed the capture. Evaluators run
        // independently and must not inherit the closing turn's accounting context.
        var samplingOverride = runtime.RunConfig.Get()?.SamplingRate;
        foreach (var registration in registrations)
        {
            // Sampling check
            var samplingRate = samplingOverride ?? registration.SamplingRate;
            if (samplingRate < 1.0 && _rng.NextDouble() > samplingRate)
                continue;

            var reg = registration;
            var tCtx = turnCtx;

            using (ExecutionContext.SuppressFlow())
            {
                _ = Task.Run(async () =>
                {
                    await RunEvaluatorAsync(reg, tCtx, conversationHistory, runtime, samplingRate, CancellationToken.None)
                        .ConfigureAwait(false);
                }, CancellationToken.None);
            }
        }
    }

    // ── Event subscription ────────────────────────────────────────────────────

    public async ValueTask HandleAsync(AgentEvent evt)
    {
        try
        {
            await _capture.HandleAsync(evt).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var messageTurnId = evt switch
            {
                MessageTurnFinishedEvent finished => finished.MessageTurnId,
                MessageTurnErrorEvent error => error.MessageTurnId,
                _ => null
            };
            _capture.Fail(evt.TraceId, messageTurnId, ex);
            // Evaluation is optional and must not fail event publication for the agent turn.
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RunEvaluatorAsync(
        EvaluatorRegistration registration,
        TurnEvaluationContext turnCtx,
        IReadOnlyList<ChatMessage> conversationHistory,
        LiveEvaluationExecutionSnapshot runtime,
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
        IEvaluationJudgeClientSession? judgeSession = null;
        if (registration.Evaluator is not HpdDeterministicEvaluatorBase &&
            registration.Evaluator is not TaskOracleEvaluator)
        {
            var judgeConfig = runtime.RunConfig.GetEvalJudgeConfigOverride() ??
                registration.JudgeConfig ??
                GlobalJudgeConfig ??
                new EvaluationJudgeRunConfig();
            judgeSession = runtime.JudgeClients.Create(judgeConfig);
            chatConfig = EvaluationExecutionHelpers.BuildChatConfiguration(judgeConfig, judgeSession.Client);
        }
        await using var ownedJudgeSession = judgeSession;

        // Build timeout CTS
        var judgeConfig2 = runtime.RunConfig.GetEvalJudgeConfigOverride() ??
            registration.JudgeConfig ??
            GlobalJudgeConfig;
        var timeout = judgeConfig2?.Timeout ?? TimeSpan.FromSeconds(30);
        using var cts = new CancellationTokenSource(timeout);

        EvaluationResult result;
        var evaluatorStart = DateTimeOffset.UtcNow;
        using var traceScope = EvalTraceContext.Activate(evaluatorName);

        try
        {
            result = await registration.Evaluator.EvaluateAsync(
                messages: turnCtx.EvaluationMessages,
                modelResponse: turnCtx.FinalResponse,
                chatConfiguration: chatConfig,
                additionalContext: additionalContext,
                cancellationToken: cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            var errorMessage = $"Evaluator '{evaluatorName}' timed out after {timeout}.";

            await runtime.PublishAsync(new EvalFailedEvent
            {
                EvaluatorName = evaluatorName,
                SessionId = turnCtx.SessionId,
                ThreadId = turnCtx.ThreadId,
                TurnIndex = turnCtx.TurnIndex,
                ErrorMessage = errorMessage,
                TimedOut = true,
            }, ct).ConfigureAwait(false);
            return;
        }
        catch (Exception ex)
        {
            var errorMessage = ex.Message;
            await runtime.PublishAsync(new EvalFailedEvent
            {
                EvaluatorName = evaluatorName,
                SessionId = turnCtx.SessionId,
                ThreadId = turnCtx.ThreadId,
                TurnIndex = turnCtx.TurnIndex,
                ErrorMessage = errorMessage,
                TimedOut = false,
                Exception = ex,
            }, ct).ConfigureAwait(false);
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
        await runtime.PublishAsync(new EvalScoreEvent
        {
            EvaluatorName = evaluatorName,
            EvaluatorVersion = version,
            Result = result,
            Source = EvaluationSource.Live,
            SessionId = turnCtx.SessionId,
            ThreadId = turnCtx.ThreadId,
            TurnIndex = turnCtx.TurnIndex,
            EvaluatorDuration = evaluatorDuration,
        }, ct).ConfigureAwait(false);

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
                        runtime,
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
                    await runtime.PublishAsync(new EvalPolicyViolationEvent
                    {
                        EvaluatorName = evaluatorName,
                        MetricName = metricName,
                        SessionId = turnCtx.SessionId,
                        ThreadId = turnCtx.ThreadId,
                        TurnIndex = turnCtx.TurnIndex,
                        Result = result,
                    }, ct).ConfigureAwait(false);
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
        LiveEvaluationExecutionSnapshot runtime,
        CancellationToken ct)
    {
        if (AnnotationQueue is null)
            return;

        AnnotationResponseEvent response;
        try
        {
            response = await runtime.RequestAnnotationAsync(request, AnnotationQueue.LockTimeout).ConfigureAwait(false);
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
