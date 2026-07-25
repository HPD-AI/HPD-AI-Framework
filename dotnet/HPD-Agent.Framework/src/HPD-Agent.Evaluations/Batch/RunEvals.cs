// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Middleware;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Integration;
using HPD.Agent.Evaluations.RedTeam;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Batch;

/// <summary>
/// Options for RunEvals batch evaluation runs.
/// </summary>
public class RunEvalsOptions
{
    /// <summary>
    /// Optional base run config copied for each case/repeat. The copy is always
    /// forced to DisableEvaluators = true so live evaluators do not double-fire.
    /// </summary>
    public AgentRunConfig? BaseRunConfig { get; init; }

    /// <summary>Number of cases to run concurrently. Default: 1 (sequential).</summary>
    public int Concurrency { get; init; } = 1;

    /// <summary>
    /// Number of evaluators to run concurrently within each case. Default: 1 preserves
    /// deterministic sequential behavior; set higher to fan out independent judges.
    /// </summary>
    public int EvaluatorConcurrency { get; init; } = 1;

    /// <summary>Number of times to repeat each case. Default: 1.</summary>
    public int Repeat { get; init; } = 1;

    /// <summary>Whether to write results to the agent's registered IScoreStore. Default: false.</summary>
    public bool PersistResults { get; init; } = false;

    /// <summary>Judge LLM configuration for evaluator calls.</summary>
    public EvalJudgeConfig? JudgeConfig { get; init; }

    /// <summary>Retry policy for agent-side 429/503 errors. Reuses ErrorHandlingConfig.</summary>
    public ErrorHandlingConfig? TaskRetryPolicy { get; init; }

    /// <summary>Store used when PersistResults is true.</summary>
    public IScoreStore? ScoreStore { get; init; }

    /// <summary>Optional dataset registry used to register immutable dataset versions before execution.</summary>
    public IDatasetStore? DatasetStore { get; init; }

    /// <summary>
    /// When true and DatasetStore is set, RunEvals registers the dataset version before
    /// running cases. Existing identical versions are accepted; conflicting versions fail early.
    /// </summary>
    public bool RegisterDatasetVersion { get; init; } = true;

    /// <summary>
    /// Optional per-evaluator policy override for MustAlwaysPass / TrackTrend enforcement.
    /// If an evaluator is not present in this dictionary, the default applies:
    /// MustAlwaysPass for HpdDeterministicEvaluatorBase subclasses, TrackTrend for all others.
    /// </summary>
    public IDictionary<IEvaluator, EvalPolicy>? EvaluatorPolicies { get; init; }
}

/// <summary>Typed options for RunEvals.</summary>
public sealed class RunEvalsOptions<TInput> : RunEvalsOptions
    where TInput : notnull
{
    /// <summary>
    /// Called after each case completes with the original typed EvalCase.
    /// Provides access to input, ground truth, metadata, and version provenance.
    /// </summary>
    public Action<EvalCase<TInput>, EvaluationReport>? OnCaseComplete { get; init; }

    /// <summary>Options used when auto-registering the dataset version.</summary>
    public DatasetRegistrationOptions<TInput>? DatasetRegistrationOptions { get; init; }
}

/// <summary>
/// Batch evaluation runner: runs an agent against a dataset of test cases,
/// applying evaluators to each response and aggregating results into an EvaluationReport.
///
/// DisableEvaluators is automatically set on internal AgentRunConfigs to prevent
/// live evaluation middleware from double-firing during batch runs.
/// </summary>
public static class RunEvals
{
    /// <summary>
    /// Execute a batch evaluation run.
    /// </summary>
    public static async Task<EvaluationReport> ExecuteAsync<TInput>(
        HPD.Agent.Agent agent,
        Dataset<TInput> dataset,
        IReadOnlyList<IEvaluator>? evaluators = null,
        RunEvalsOptions<TInput>? options = null,
        string? experimentName = null,
        CancellationToken ct = default)
        where TInput : notnull
    {
        options ??= new();
        evaluators ??= [];

        if (options.RegisterDatasetVersion &&
            options.DatasetStore is not null &&
            !string.IsNullOrWhiteSpace(dataset.DatasetId) &&
            !string.IsNullOrWhiteSpace(dataset.Version))
        {
            await options.DatasetStore.RegisterDatasetVersionAsync(
                dataset,
                options.DatasetRegistrationOptions,
                ct).ConfigureAwait(false);
        }

        var allEvaluators = dataset.Evaluators.Concat(evaluators).ToList();
        var cases = new ConcurrentBag<ReportCase>();
        var failures = new ConcurrentBag<ReportCaseFailure>();

        var judgeConfig = options.JudgeConfig;

        var semaphore = new SemaphoreSlim(Math.Max(1, options.Concurrency));
        var tasks = new List<Task>();

        for (int caseIdx = 0; caseIdx < dataset.Cases.Count; caseIdx++)
        {
            var evalCase = dataset.Cases[caseIdx];
            var caseName = evalCase.Name ?? $"case-{caseIdx}";

            for (int repeatIdx = 0; repeatIdx < Math.Max(1, options.Repeat); repeatIdx++)
            {
                var localCase = evalCase;
                var localName = options.Repeat > 1 ? $"{caseName}[{repeatIdx}]" : caseName;
                var localRepeat = repeatIdx;

                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        var reportCase = await RunSingleCaseAsync(
                            agent, localCase, localName, caseName, (localRepeat + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), allEvaluators,
                            dataset.DatasetId, dataset.Version,
                            judgeConfig, options, experimentName, ct).ConfigureAwait(false);

                        cases.Add(reportCase);

                        if (options.OnCaseComplete is not null)
                        {
                            var singleReport = new EvaluationReport(localName, [reportCase]);
                            options.OnCaseComplete(localCase, singleReport);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        var kind = EvaluationExecutionHelpers.IsInfrastructureError(ex)
                            ? FailureKind.InfrastructureError
                            : FailureKind.TaskFailure;
                        failures.Add(new ReportCaseFailure(localName, kind, ex.Message));
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct));
            }
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var allCases = cases.ToList();
        var report = new EvaluationReport(
            experimentName ?? $"eval-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            allCases,
            failures.ToList());

        // Run report-level evaluators
        var allReportEvaluators = dataset.ReportEvaluators.ToList();
        var analyses = allReportEvaluators.SelectMany(re => re.Evaluate(report)).ToList();

        // Check MustAlwaysPass policies.
        // Policy resolution order:
        //   1. options.EvaluatorPolicies[evaluator] — explicit per-evaluator override
        //   2. Default: MustAlwaysPass for deterministic evaluators (HpdDeterministicEvaluatorBase),
        //      TrackTrend for all others (LLM judge scores are probabilistic by nature).
        foreach (var evaluator in allEvaluators)
        {
            var policy = ResolvePolicy(evaluator, options);

            if (policy != EvalPolicy.MustAlwaysPass)
                continue;

            var metricName = evaluator.EvaluationMetricNames.FirstOrDefault();
            if (metricName is null) continue;

            double passRate = report.PassRate(metricName);
            if (passRate < 1.0)
                report.AddPolicyViolation(evaluator, passRate);
        }

        return report;
    }

    private static async Task<ReportCase> RunSingleCaseAsync<TInput>(
        HPD.Agent.Agent agent,
        EvalCase<TInput> evalCase,
        string caseName,
        string scenarioName,
        string iterationName,
        IReadOnlyList<IEvaluator> evaluators,
        string? datasetId,
        string? datasetVersion,
        EvalJudgeConfig? judgeConfig,
        RunEvalsOptions options,
        string? experimentName,
        CancellationToken ct)
        where TInput : notnull
    {
        var taskStart = DateTimeOffset.UtcNow;

        // Build the case-level AgentRunConfig with DisableEvaluators to prevent
        // live double-firing. Each retry attempt clones this config before the
        // capture middleware adds per-attempt request state.
        var caseRunConfig = CloneRunConfig(options.BaseRunConfig);
        caseRunConfig.DisableEvaluators = true;
        caseRunConfig.UserMessage = evalCase.Input?.ToString() ?? string.Empty;

        if (evalCase.GroundTruth is not null)
        {
            caseRunConfig.ContextOverrides ??= new Dictionary<string, object>();
            caseRunConfig.ContextOverrides["groundTruth"] = evalCase.GroundTruth;
        }

        TurnEvaluationContext turnCtx;
        AgentRunConfig? runConfig = null;
        try
        {
            turnCtx = await ExecuteWithRetryAsync(
                () =>
                {
                    var attemptRunConfig = CloneRunConfig(caseRunConfig);
                    runConfig = attemptRunConfig;
                    return RunAgentAndCaptureAsync(agent, attemptRunConfig, ct);
                },
                options.TaskRetryPolicy,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (EvaluationExecutionHelpers.IsInfrastructureError(ex)) throw;
            // Task failure — return a case with an error result
            return new ReportCase(
                caseName,
                caseRunConfig.ProviderKey,
                caseRunConfig.ModelId,
                null,
                new EvaluationResult(),
                [new EvaluatorFailure("Agent", ex.Message)],
                DateTimeOffset.UtcNow - taskStart,
                TimeSpan.Zero,
                DateTimeOffset.UtcNow - taskStart);
        }

        var taskDuration = DateTimeOffset.UtcNow - taskStart;
        turnCtx = NormalizeBatchContext(
            turnCtx,
            agent.GetType().Name,
            experimentName,
            caseName,
            evalCase.Input?.ToString() ?? string.Empty,
            evalCase.GroundTruth,
            runConfig ?? caseRunConfig,
            taskDuration);
        var agentResponse = turnCtx.FinalResponse;

        // Build messages for evaluators from the actual evaluated turn.
        var messages = turnCtx.ConversationHistory
            .Concat([new ChatMessage(ChatRole.User, turnCtx.UserInput)])
            .ToList();

        // Build additional context
        var additionalContext = new List<EvaluationContext>();
        var groundTruth = turnCtx.GroundTruth ?? evalCase.GroundTruth;
        if (groundTruth is not null)
            additionalContext.Add(new GroundTruthContext(groundTruth));
        additionalContext.Add(new TurnEvaluationContextWrapper(turnCtx));

        var evalStart = DateTimeOffset.UtcNow;
        var caseEvaluators = evaluators
            .Concat(evalCase.Evaluators ?? [])
            .ToList();

        var evaluatorResults = new EvaluationResult?[caseEvaluators.Count];
        var evaluatorJudgeCalls = new IReadOnlyList<JudgeCallRecord>?[caseEvaluators.Count];
        var evaluatorFailures = new EvaluatorFailure?[caseEvaluators.Count];
        var evaluatorConcurrency = Math.Max(1, options.EvaluatorConcurrency);
        using var evaluatorSemaphore = new SemaphoreSlim(evaluatorConcurrency);

        var evaluatorTasks = caseEvaluators.Select((evaluator, index) => Task.Run(async () =>
        {
            await evaluatorSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var output = await RunEvaluatorAsync(
                    evaluator,
                    messages,
                    agentResponse,
                    additionalContext,
                    turnCtx,
                    evalCase,
                    datasetId,
                    datasetVersion,
                    runConfig ?? caseRunConfig,
                    judgeConfig,
                    options,
                    ct).ConfigureAwait(false);

                evaluatorResults[index] = output.Result;
                evaluatorJudgeCalls[index] = output.JudgeCalls;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                evaluatorFailures[index] = new EvaluatorFailure(evaluator.GetType().Name, ex.Message);
            }
            finally
            {
                evaluatorSemaphore.Release();
            }
        }, ct)).ToList();

        await Task.WhenAll(evaluatorTasks).ConfigureAwait(false);

        var evalDuration = DateTimeOffset.UtcNow - evalStart;
        var evalResults = evaluatorResults.Where(r => r is not null).Cast<EvaluationResult>().ToList();
        var judgeCalls = evaluatorJudgeCalls
            .Where(calls => calls is not null)
            .SelectMany(calls => calls!)
            .ToList();
        var evalFailures = evaluatorFailures.Where(f => f is not null).Cast<EvaluatorFailure>().ToList();
        var mergedResult = MergeResults(evalResults);

        if (options.PersistResults && options.ScoreStore is not null)
        {
            await options.ScoreStore.WriteRunAsync(new EvaluationRunRecord
            {
                Id = Guid.NewGuid().ToString(),
                ExecutionName = experimentName ?? "eval",
                ScenarioName = scenarioName,
                IterationName = iterationName,
                CreatedAt = DateTimeOffset.UtcNow,
                Messages = messages,
                ModelResponse = agentResponse,
                EvaluationResult = mergedResult,
                JudgeCalls = judgeCalls,
                Tags = BuildRunTags(datasetId, datasetVersion, evalCase),
                Metadata = evalCase.Metadata is null
                    ? null
                    : new Dictionary<string, object>(evalCase.Metadata),
                Source = EvaluationSource.Test,
                AgentName = turnCtx.AgentName,
                SessionId = turnCtx.SessionId,
                ThreadId = turnCtx.ThreadId,
                TurnIndex = turnCtx.TurnIndex,
                ProviderKey = turnCtx.ProviderKey,
                ModelId = turnCtx.ModelId,
                ResponseModelId = turnCtx.ResponseModelId,
                DatasetId = datasetId,
                DatasetVersion = datasetVersion,
                CaseId = evalCase.CaseId ?? evalCase.Name,
                CaseVersion = evalCase.Version,
                CaseValidFrom = evalCase.ValidFrom,
                CaseValidTo = evalCase.ValidTo,
                TaskDuration = taskDuration,
                EvaluatorDuration = evalDuration,
                TotalDuration = taskDuration + evalDuration,
            }, ct).ConfigureAwait(false);
        }

        return new ReportCase(
            caseName,
            turnCtx.ProviderKey,
            turnCtx.ModelId,
            turnCtx.ResponseModelId,
            mergedResult,
            evalFailures,
            taskDuration,
            evalDuration,
            taskDuration + evalDuration);
    }

    private static async Task<TurnEvaluationContext> RunAgentAndCaptureAsync(
        HPD.Agent.Agent agent,
        AgentRunConfig runConfig,
        CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString();
        var capture = new BatchEvalCaptureMiddleware();

        runConfig.ContextOverrides ??= new Dictionary<string, object>();
        runConfig.ContextOverrides[BatchEvalCaptureMiddleware.CaptureRequestIdKey] = requestId;
        runConfig.RuntimeMiddleware = PrependRuntimeMiddleware(runConfig.RuntimeMiddleware, capture);

        using var subscription = agent.SubscribeAny(capture.HandleAsync);

        await agent.RunAsync(new HPD.Agent.UserMessagesInputEvent
        {
            Messages = [
                new ChatMessage(ChatRole.User, runConfig.UserMessage ?? string.Empty)
            ],
            RunConfig = runConfig,
        }, ct).ConfigureAwait(false);

        return await capture.Captured.WaitAsync(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<IAgentMiddleware> PrependRuntimeMiddleware(
        IReadOnlyList<IAgentMiddleware>? existing,
        IAgentMiddleware middleware)
    {
        if (existing is not { Count: > 0 })
            return [middleware];

        var merged = new List<IAgentMiddleware>(existing.Count + 1);
        merged.Add(middleware);
        merged.AddRange(existing);
        return merged;
    }

    private static IReadOnlyList<string> BuildRunTags<TInput>(
        string? datasetId,
        string? datasetVersion,
        EvalCase<TInput> evalCase)
        where TInput : notnull
    {
        var tags = new List<string>();

        if (!string.IsNullOrWhiteSpace(datasetId))
            tags.Add($"dataset:{datasetId}");
        if (!string.IsNullOrWhiteSpace(datasetVersion))
            tags.Add($"dataset-version:{datasetVersion}");
        if (!string.IsNullOrWhiteSpace(evalCase.CaseId))
            tags.Add($"case:{evalCase.CaseId}");
        if (!string.IsNullOrWhiteSpace(evalCase.Version))
            tags.Add($"case-version:{evalCase.Version}");

        return tags;
    }

    private static async Task<EvaluatorRunOutput> RunEvaluatorAsync<TInput>(
        IEvaluator evaluator,
        IReadOnlyList<ChatMessage> messages,
        ChatResponse agentResponse,
        IReadOnlyList<EvaluationContext> additionalContext,
        TurnEvaluationContext turnCtx,
        EvalCase<TInput> evalCase,
        string? datasetId,
        string? datasetVersion,
        AgentRunConfig runConfig,
        EvalJudgeConfig? judgeConfig,
        RunEvalsOptions options,
        CancellationToken ct)
        where TInput : notnull
    {
        var evaluatorName = evaluator.GetType().Name;
        using var traceScope = EvalTraceContext.Activate(evaluatorName);

        var chatConfig = NeedsJudgeChatConfiguration(evaluator)
            ? EvaluationExecutionHelpers.BuildChatConfiguration(judgeConfig)
            : null;

        var result = await evaluator.EvaluateAsync(
            messages, agentResponse, chatConfig, additionalContext, ct)
            .ConfigureAwait(false);
        var judgeCalls = traceScope.Snapshot();

        if (options.PersistResults && options.ScoreStore is not null)
        {
            var (judgeModelId, judgeUsage, judgeDuration) =
                EvaluationExecutionHelpers.ExtractJudgeMetadata(result);
            await options.ScoreStore.WriteScoreAsync(new ScoreRecord
            {
                Id = Guid.NewGuid().ToString(),
                EvaluatorName = evaluatorName,
                EvaluatorVersion = EvaluationExecutionHelpers.ResolveEvaluatorVersion(evaluator),
                Result = result,
                Source = EvaluationSource.Test,
                SessionId = turnCtx.SessionId,
                ThreadId = turnCtx.ThreadId,
                TurnIndex = turnCtx.TurnIndex,
                AgentName = turnCtx.AgentName,
                ProviderKey = turnCtx.ProviderKey,
                ModelId = turnCtx.ModelId,
                ResponseModelId = turnCtx.ResponseModelId,
                DatasetId = datasetId,
                DatasetVersion = datasetVersion,
                CaseId = evalCase.CaseId ?? evalCase.Name,
                CaseVersion = evalCase.Version,
                CaseValidFrom = evalCase.ValidFrom,
                CaseValidTo = evalCase.ValidTo,
                RedTeamPluginId = TryGetMetadataString(evalCase.Metadata, RedTeamCaseExtensions.MetadataPluginId),
                RedTeamStrategyId = TryGetMetadataString(evalCase.Metadata, RedTeamCaseExtensions.MetadataStrategyId),
                RedTeamCategory = TryGetMetadataString(evalCase.Metadata, RedTeamCaseExtensions.MetadataCategory),
                RedTeamSeverity = TryGetMetadataString(evalCase.Metadata, RedTeamCaseExtensions.MetadataSeverity),
                AttackGoal = TryGetMetadataString(evalCase.Metadata, RedTeamCaseExtensions.MetadataGoal),
                AttackSucceeded = IsRedTeamCase(evalCase.Metadata)
                    ? !IsPassingResult(result)
                    : null,
                TurnUsage = turnCtx.TurnUsage,
                TurnDuration = turnCtx.Duration,
                Attributes = turnCtx.Attributes,
                Metrics = turnCtx.Metrics,
                JudgeModelId = judgeModelId,
                JudgeUsage = judgeUsage,
                JudgeDuration = judgeDuration,
                JudgeCalls = judgeCalls,
                SamplingRate = 1.0,
                Policy = ResolvePolicy(evaluator, options),
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct).ConfigureAwait(false);
        }

        return new EvaluatorRunOutput(result, judgeCalls);
    }

    private sealed record EvaluatorRunOutput(
        EvaluationResult Result,
        IReadOnlyList<JudgeCallRecord> JudgeCalls);

    private static TurnEvaluationContext NormalizeBatchContext(
        TurnEvaluationContext source,
        string fallbackAgentName,
        string? experimentName,
        string caseName,
        string userInput,
        string? groundTruth,
        AgentRunConfig runConfig,
        TimeSpan taskDuration)
    {
        ArgumentNullException.ThrowIfNull(source);
        var attributes = BuildBatchAttributes(source);

        return new TurnEvaluationContext
        {
            AgentName = string.IsNullOrWhiteSpace(source.AgentName) ? fallbackAgentName : source.AgentName,
            // Batch identity intentionally wins here: score storage groups batch
            // results by experiment/case through SessionId and ThreadId.
            SessionId = experimentName ?? "eval",
            ThreadId = caseName,
            ConversationId = caseName,
            TurnIndex = source.TurnIndex,
            UserInput = string.IsNullOrEmpty(source.UserInput) ? userInput : source.UserInput,
            ConversationHistory = source.ConversationHistory,
            OutputText = source.OutputText,
            FinalResponse = source.FinalResponse,
            ReasoningText = source.ReasoningText,
            ToolCalls = source.ToolCalls,
            Trace = source.Trace,
            TurnUsage = source.TurnUsage,
            IterationUsage = source.IterationUsage,
            IterationCount = source.IterationCount,
            Duration = source.Duration == TimeSpan.Zero ? taskDuration : source.Duration,
            ModelId = source.ModelId ?? runConfig.ModelId ?? source.FinalResponse?.ModelId,
            ResponseModelId = source.ResponseModelId ?? source.FinalResponse?.ModelId,
            ProviderKey = source.ProviderKey ?? runConfig.ProviderKey,
            Attributes = attributes,
            Metrics = source.Metrics,
            StopKind = source.StopKind,
            GroundTruth = source.GroundTruth ?? groundTruth,
            ExperimentContext = SanitizeExperimentContext(source.ExperimentContext ?? runConfig.ContextOverrides),
        };
    }

    private static IDictionary<string, object>? SanitizeExperimentContext(
        IDictionary<string, object>? context)
    {
        if (context is null || !context.ContainsKey(BatchEvalCaptureMiddleware.CaptureRequestIdKey))
            return context;

        var sanitized = new Dictionary<string, object>(context);
        sanitized.Remove(BatchEvalCaptureMiddleware.CaptureRequestIdKey);
        return sanitized;
    }

    private static IReadOnlyDictionary<string, object> BuildBatchAttributes(TurnEvaluationContext source)
    {
        var attributes = new Dictionary<string, object>(source.Attributes);

        if (source.ToolCalls.Count > 0 && !attributes.ContainsKey("tool_calls"))
            attributes["tool_calls"] = source.ToolCalls;

        if (!string.IsNullOrWhiteSpace(source.SessionId))
            attributes.TryAdd("source_session_id", source.SessionId);
        if (!string.IsNullOrWhiteSpace(source.ThreadId))
            attributes.TryAdd("source_thread_id", source.ThreadId);
        if (!string.IsNullOrWhiteSpace(source.ConversationId))
            attributes.TryAdd("source_conversation_id", source.ConversationId);

        return attributes;
    }

    private static EvaluationResult MergeResults(List<EvaluationResult> results)
    {
        var merged = new EvaluationResult();
        foreach (var result in results)
        foreach (var (name, metric) in result.Metrics)
            merged.Metrics[name] = metric;
        return merged;
    }

    private static EvalPolicy ResolvePolicy(IEvaluator evaluator, RunEvalsOptions options)
    {
        if (options.EvaluatorPolicies is not null &&
            options.EvaluatorPolicies.TryGetValue(evaluator, out var explicitPolicy))
            return explicitPolicy;

        return evaluator is HpdDeterministicEvaluatorBase
            ? EvalPolicy.MustAlwaysPass
            : EvalPolicy.TrackTrend;
    }

    private static bool IsRedTeamCase(IDictionary<string, object>? metadata)
        => !string.IsNullOrWhiteSpace(TryGetMetadataString(metadata, RedTeamCaseExtensions.MetadataPluginId));

    private static string? TryGetMetadataString(IDictionary<string, object>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value) || value is null)
            return null;

        if (value is string text)
            return string.IsNullOrWhiteSpace(text) ? null : text;

        return value.ToString();
    }

    private static bool IsPassingResult(EvaluationResult result)
    {
        foreach (var (_, metric) in result.Metrics)
        {
            if (metric is BooleanMetric bm && bm.Value == false)
                return false;
            if (metric is NumericMetric nm && nm.Value.HasValue && nm.Value.Value < 0.5)
                return false;
        }

        return true;
    }

    private static bool NeedsJudgeChatConfiguration(IEvaluator evaluator) =>
        evaluator is not HpdDeterministicEvaluatorBase &&
        evaluator is not TaskOracleEvaluator;

    private static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        ErrorHandlingConfig? retryPolicy,
        CancellationToken ct)
    {
        if (retryPolicy is null || retryPolicy.MaxRetries <= 0)
            return await action().ConfigureAwait(false);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException &&
                                       EvaluationExecutionHelpers.IsInfrastructureError(ex) &&
                                       attempt < retryPolicy.MaxRetries)
            {
                var delay = GetRetryDelay(retryPolicy, attempt + 1);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static TimeSpan GetRetryDelay(ErrorHandlingConfig policy, int attempt)
    {
        var delayMs = policy.RetryDelay.TotalMilliseconds *
                      Math.Pow(policy.BackoffMultiplier, Math.Max(0, attempt - 1));
        var capped = Math.Min(delayMs, policy.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
    }

    private static AgentRunConfig CloneRunConfig(AgentRunConfig? source)
    {
        if (source is null)
            return new AgentRunConfig();

        return new AgentRunConfig
        {
            Security = source.Security with { },
            Sandbox = source.Sandbox with
            {
                Filesystem = source.Sandbox.Filesystem
                    .Select(static grant => grant with { })
                    .ToArray()
            },
            Chat = source.Chat,
            ProviderKey = source.ProviderKey,
            ModelId = source.ModelId,
            ApiKey = source.ApiKey,
            ProviderEndpoint = source.ProviderEndpoint,
            CustomHeaders = source.CustomHeaders is null ? null : new(source.CustomHeaders),
            ProviderOptions = source.ProviderOptions,
            OverrideChatClient = source.OverrideChatClient,
            SystemInstructions = source.SystemInstructions,
            AdditionalSystemInstructions = source.AdditionalSystemInstructions,
            ContextOverrides = source.ContextOverrides is null ? null : new(source.ContextOverrides),
            RunTimeout = source.RunTimeout,
            UseCache = source.UseCache,
            SkipTools = source.SkipTools,
            CoalesceDeltas = source.CoalesceDeltas,
            RuntimeMiddleware = source.RuntimeMiddleware,
            PermissionOverrides = source.PermissionOverrides is null ? null : new(source.PermissionOverrides),
            ClientToolInput = source.ClientToolInput,
            ClientAppProviders = source.ClientAppProviders is null ? null : new(source.ClientAppProviders),
            ConversationIdOverride = source.ConversationIdOverride,
            CustomStreamCallback = source.CustomStreamCallback,
            ContextInstances = source.ContextInstances is null ? null : new(source.ContextInstances),
            AllowBackgroundResponses = source.AllowBackgroundResponses,
            ContinuationToken = source.ContinuationToken,
            BackgroundPollingInterval = source.BackgroundPollingInterval,
            BackgroundTimeout = source.BackgroundTimeout,
            Attachments = source.Attachments,
            Audio = source.Audio,
            Compaction = source.Compaction,
            UserMessage = source.UserMessage,
            DisableEvaluators = source.DisableEvaluators,
            IsInternalEvalJudgeCall = source.IsInternalEvalJudgeCall,
            AdditionalEvaluators = source.AdditionalEvaluators,
            EvaluatorSamplingOverride = source.EvaluatorSamplingOverride,
            EvalJudgeConfigOverride = source.EvalJudgeConfigOverride,
        };
    }
}
