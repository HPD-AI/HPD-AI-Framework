// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Diagnostics.CodeAnalysis;
using System.Text;
using HPD.Agent;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Batch;
using HPD.Agent.Evaluations.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;

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
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);

        var middleware = GetOrCreateMiddleware(builder);
        middleware.ScoreStore = store;
        return builder;
    }

    /// <summary>
    /// Sets LiveEvaluationMiddleware to persist scores into the same workspace substrate
    /// used by the builder's runtime repositories.
    /// </summary>
    public static AgentBuilder UseWorkspaceScoreStore(
        this AgentBuilder builder,
        IWorkspaceStore? workspace = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (workspace is not null)
            EnsureWorkspaceRepositories(builder, workspace);

        return builder.UseScoreStore(new BuilderWorkspaceScoreStore(builder, workspace));
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

    private static IWorkspaceStore ResolveWorkspaceForEvaluationStore(AgentBuilder builder)
    {
        var sessionWorkspace = builder.Config.SessionRepository is WorkspaceSessionRepository sessionRepository
            ? sessionRepository.Workspace
            : null;
        var agentWorkspace = builder.Config.AgentRepository is WorkspaceAgentRepository agentRepository
            ? agentRepository.Workspace
            : null;

        if (sessionWorkspace is not null &&
            agentWorkspace is not null &&
            !ReferenceEquals(sessionWorkspace, agentWorkspace))
        {
            throw new InvalidOperationException(
                "Cannot infer a workspace score store because the builder uses different session and agent workspaces.");
        }

        if (sessionWorkspace is not null)
            return sessionWorkspace;

        if (agentWorkspace is not null)
            return agentWorkspace;

        if (builder.Config.SessionRepository is not null || builder.Config.AgentRepository is not null)
        {
            throw new InvalidOperationException(
                "Cannot infer a workspace score store from non-workspace runtime repositories. Pass an explicit IWorkspaceStore.");
        }

        return new InMemoryWorkspaceStore();
    }

    private static void EnsureWorkspaceRepositories(AgentBuilder builder, IWorkspaceStore workspace)
    {
        if (builder.Config.SessionRepository is WorkspaceSessionRepository sessionRepository &&
            !ReferenceEquals(sessionRepository.Workspace, workspace))
        {
            throw new InvalidOperationException(
                "The supplied workspace does not match the builder's session repository workspace.");
        }

        if (builder.Config.AgentRepository is WorkspaceAgentRepository agentRepository &&
            !ReferenceEquals(agentRepository.Workspace, workspace))
        {
            throw new InvalidOperationException(
                "The supplied workspace does not match the builder's agent repository workspace.");
        }

        builder.Config.SessionRepository ??= new WorkspaceSessionRepository(workspace);
        builder.Config.AgentRepository ??= new WorkspaceAgentRepository(workspace);
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
                config.SkipTools = true;

                await judgeAgent.RunAsync(new UserTextInputEvent(userMessage)
                {
                    RunConfig = config,
                }, ct).ConfigureAwait(false);

                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                waitCts.CancelAfter(config.RunTimeout ?? TimeSpan.FromSeconds(30));
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

    private sealed class BuilderWorkspaceScoreStore(
        AgentBuilder builder,
        IWorkspaceStore? explicitWorkspace) : IScoreStore
    {
        private WorkspaceScoreStore Resolve()
        {
            var workspace = explicitWorkspace ?? ResolveWorkspaceForEvaluationStore(builder);
            EnsureWorkspaceRepositories(builder, workspace);
            return new WorkspaceScoreStore(workspace);
        }

        public ValueTask WriteScoreAsync(ScoreRecord record, CancellationToken ct = default) =>
            Resolve().WriteScoreAsync(record, ct);

        public ValueTask WriteRunAsync(EvaluationRunRecord record, CancellationToken ct = default) =>
            Resolve().WriteRunAsync(record, ct);

        public IAsyncEnumerable<ScoreRecord> GetScoresAsync(
            string sessionId,
            string? branchId = null,
            CancellationToken ct = default) =>
            Resolve().GetScoresAsync(sessionId, branchId, ct);

        public IAsyncEnumerable<ScoreRecord> GetScoresAsync(
            string evaluatorName,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetScoresAsync(evaluatorName, from, to, ct);

        public IAsyncEnumerable<EvaluationRunRecord> GetRunsAsync(
            string? executionName = null,
            string? scenarioName = null,
            string? iterationName = null,
            CancellationToken ct = default) =>
            Resolve().GetRunsAsync(executionName, scenarioName, iterationName, ct);

        public ValueTask DeleteRunsAsync(
            string? executionName = null,
            string? scenarioName = null,
            string? iterationName = null,
            CancellationToken ct = default) =>
            Resolve().DeleteRunsAsync(executionName, scenarioName, iterationName, ct);

        public IAsyncEnumerable<string> GetLatestRunExecutionNamesAsync(
            int? count = null,
            CancellationToken ct = default) =>
            Resolve().GetLatestRunExecutionNamesAsync(count, ct);

        public IAsyncEnumerable<string> GetRunScenarioNamesAsync(
            string executionName,
            CancellationToken ct = default) =>
            Resolve().GetRunScenarioNamesAsync(executionName, ct);

        public IAsyncEnumerable<string> GetRunIterationNamesAsync(
            string executionName,
            string scenarioName,
            CancellationToken ct = default) =>
            Resolve().GetRunIterationNamesAsync(executionName, scenarioName, ct);

        public ValueTask<ScoreTrend> GetTrendAsync(
            string evaluatorName,
            DateTimeOffset from,
            DateTimeOffset to,
            TimeSpan bucketSize,
            CancellationToken ct = default) =>
            Resolve().GetTrendAsync(evaluatorName, from, to, bucketSize, ct);

        public ValueTask<double> GetPassRateAsync(
            string evaluatorName,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetPassRateAsync(evaluatorName, from, to, ct);

        public ValueTask<IDictionary<string, ScoreAggregate>> GetAgentComparisonAsync(
            string evaluatorName,
            IEnumerable<string> agentNames,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetAgentComparisonAsync(evaluatorName, agentNames, from, to, ct);

        public ValueTask<BranchComparisonResult> GetBranchComparisonAsync(
            string sessionId,
            string branchId1,
            string branchId2,
            IEnumerable<string> evaluatorNames,
            CancellationToken ct = default) =>
            Resolve().GetBranchComparisonAsync(sessionId, branchId1, branchId2, evaluatorNames, ct);

        public ValueTask<IReadOnlyList<EvaluatorSummary>> GetEvaluatorSummaryAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetEvaluatorSummaryAsync(from, to, ct);

        public ValueTask<double> GetFailureRateAsync(
            string evaluatorName,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetFailureRateAsync(evaluatorName, from, to, ct);

        public ValueTask<IDictionary<string, double>> GetCostBreakdownAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetCostBreakdownAsync(from, to, ct);

        public IAsyncEnumerable<ScoreRecord> GetScoresByVersionAsync(
            string evaluatorName,
            string version,
            CancellationToken ct = default) =>
            Resolve().GetScoresByVersionAsync(evaluatorName, version, ct);

        public ValueTask<IDictionary<string, ToolUsageSummary>> GetToolUsageSummaryAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetToolUsageSummaryAsync(from, to, ct);

        public ValueTask<IReadOnlyList<RiskAutonomyDataPoint>> GetRiskAutonomyDistributionAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetRiskAutonomyDistributionAsync(from, to, ct);

        public ValueTask<double> GetAttackSuccessRateAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetAttackSuccessRateAsync(from, to, ct);

        public ValueTask<IDictionary<string, double>> GetAttackSuccessRateByPluginAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetAttackSuccessRateByPluginAsync(from, to, ct);

        public ValueTask<IDictionary<string, double>> GetAttackSuccessRateByStrategyAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetAttackSuccessRateByStrategyAsync(from, to, ct);

        public ValueTask<IReadOnlyList<RedTeamFinding>> GetRedTeamFindingsAsync(
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            CancellationToken ct = default) =>
            Resolve().GetRedTeamFindingsAsync(from, to, ct);

        public ValueTask WriteResultsAsync(
            IEnumerable<ScenarioRunResult> results,
            CancellationToken ct = default) =>
            Resolve().WriteResultsAsync(results, ct);

        public IAsyncEnumerable<ScenarioRunResult> ReadResultsAsync(
            string? executionName,
            string? scenarioName,
            string? iterationName,
            CancellationToken ct = default) =>
            Resolve().ReadResultsAsync(executionName, scenarioName, iterationName, ct);

        public ValueTask DeleteResultsAsync(
            string? executionName,
            string? scenarioName,
            string? iterationName,
            CancellationToken ct = default) =>
            Resolve().DeleteResultsAsync(executionName, scenarioName, iterationName, ct);

        public IAsyncEnumerable<string> GetLatestExecutionNamesAsync(
            int? maxCount = null,
            CancellationToken ct = default) =>
            Resolve().GetLatestExecutionNamesAsync(maxCount, ct);

        public IAsyncEnumerable<string> GetScenarioNamesAsync(
            string? executionName,
            CancellationToken ct = default) =>
            Resolve().GetScenarioNamesAsync(executionName, ct);

        public IAsyncEnumerable<string> GetIterationNamesAsync(
            string? executionName,
            string? scenarioName,
            CancellationToken ct = default) =>
            Resolve().GetIterationNamesAsync(executionName, scenarioName, ct);
    }
}
