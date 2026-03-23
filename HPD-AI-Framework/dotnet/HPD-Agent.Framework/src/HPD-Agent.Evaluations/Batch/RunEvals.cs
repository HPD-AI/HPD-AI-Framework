// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Batch;

/// <summary>
/// Options for RunEvals batch evaluation runs.
/// </summary>
public sealed class RunEvalsOptions
{
    /// <summary>Number of cases to run concurrently. Default: 1 (sequential).</summary>
    public int Concurrency { get; init; } = 1;

    /// <summary>Number of times to repeat each case. Default: 1.</summary>
    public int Repeat { get; init; } = 1;

    /// <summary>Whether to write results to the agent's registered IScoreStore. Default: false.</summary>
    public bool PersistResults { get; init; } = false;

    /// <summary>Judge LLM configuration for evaluator calls.</summary>
    public EvalJudgeConfig? JudgeConfig { get; init; }

    /// <summary>Retry policy for agent-side 429/503 errors. Reuses ErrorHandlingConfig.</summary>
    public ErrorHandlingConfig? TaskRetryPolicy { get; init; }

    /// <summary>Retry policy for evaluator judge LLM 429/503 errors.</summary>
    public ErrorHandlingConfig? EvaluatorRetryPolicy { get; init; }

    /// <summary>Called after each case completes. Useful for progress reporting.</summary>
    public Action<string, EvaluationReport>? OnCaseComplete { get; init; }

    /// <summary>
    /// Optional per-evaluator policy override for MustAlwaysPass / TrackTrend enforcement.
    /// If an evaluator is not present in this dictionary, the default applies:
    /// MustAlwaysPass for HpdDeterministicEvaluatorBase subclasses, TrackTrend for all others.
    /// </summary>
    public IDictionary<IEvaluator, EvalPolicy>? EvaluatorPolicies { get; init; }
}

/// <summary>
/// Batch evaluation runner: runs an agent against a dataset of test cases,
/// applying evaluators to each response and aggregating results into an EvaluationReport.
///
/// DisableEvaluators is automatically set on internal AgentRunConfigs to prevent
/// live EvaluationMiddleware from double-firing during batch runs.
/// </summary>
public static class RunEvals
{
    /// <summary>
    /// Execute a batch evaluation run.
    /// </summary>
    public static async Task<EvaluationReport> ExecuteAsync<TInput>(
        IAgent agent,
        Dataset<TInput> dataset,
        IReadOnlyList<IEvaluator>? evaluators = null,
        RunEvalsOptions? options = null,
        string? experimentName = null,
        CancellationToken ct = default)
        where TInput : notnull
    {
        options ??= new();
        evaluators ??= [];

        var allEvaluators = dataset.Evaluators.Concat(evaluators).ToList();
        var cases = new ConcurrentBag<ReportCase>();
        var failures = new ConcurrentBag<ReportCaseFailure>();

        // Resolve judge ChatConfiguration from JudgeConfig if provided
        ChatConfiguration? chatConfig = options.JudgeConfig?.OverrideChatClient is not null
            ? new ChatConfiguration(options.JudgeConfig.OverrideChatClient)
            : null;

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
                            agent, localCase, localName, allEvaluators,
                            chatConfig, options, ct).ConfigureAwait(false);

                        cases.Add(reportCase);

                        if (options.OnCaseComplete is not null)
                        {
                            var singleReport = new EvaluationReport(localName, [reportCase]);
                            options.OnCaseComplete(localName, singleReport);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        var kind = IsInfrastructureError(ex)
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
            EvalPolicy policy;
            if (options.EvaluatorPolicies is not null &&
                options.EvaluatorPolicies.TryGetValue(evaluator, out var explicitPolicy))
            {
                policy = explicitPolicy;
            }
            else
            {
                // Deterministic evaluators default to MustAlwaysPass; everything else TrackTrend
                policy = evaluator is HpdDeterministicEvaluatorBase
                    ? EvalPolicy.MustAlwaysPass
                    : EvalPolicy.TrackTrend;
            }

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
        IAgent agent,
        EvalCase<TInput> evalCase,
        string caseName,
        IReadOnlyList<IEvaluator> evaluators,
        ChatConfiguration? chatConfig,
        RunEvalsOptions options,
        CancellationToken ct)
        where TInput : notnull
    {
        var taskStart = DateTimeOffset.UtcNow;

        // Build AgentRunConfig with DisableEvaluators to prevent live double-firing
        var runConfig = new AgentRunConfig
        {
            DisableEvaluators = true,
            UserMessage = evalCase.Input?.ToString() ?? string.Empty,
        };
        if (evalCase.GroundTruth is not null)
        {
            runConfig.ContextOverrides ??= new Dictionary<string, object?>();
            runConfig.ContextOverrides["groundTruth"] = evalCase.GroundTruth;
        }

        ChatResponse agentResponse;
        try
        {
            agentResponse = await agent.RunAsync(runConfig, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (IsInfrastructureError(ex)) throw;
            // Task failure — return a case with an error result
            return new ReportCase(
                caseName,
                new EvaluationResult(),
                [new EvaluatorFailure("Agent", ex.Message)],
                DateTimeOffset.UtcNow - taskStart,
                TimeSpan.Zero,
                DateTimeOffset.UtcNow - taskStart);
        }

        var taskDuration = DateTimeOffset.UtcNow - taskStart;

        // Build messages for evaluators (simple user message + response)
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, evalCase.Input?.ToString() ?? string.Empty),
        };

        // Build additional context
        var additionalContext = new List<EvaluationContext>();
        if (evalCase.GroundTruth is not null)
            additionalContext.Add(new GroundTruthContext(evalCase.GroundTruth));

        var evalStart = DateTimeOffset.UtcNow;
        var evalResults = new List<EvaluationResult>();
        var evalFailures = new List<EvaluatorFailure>();

        var caseEvaluators = evaluators
            .Concat(evalCase.Evaluators ?? [])
            .ToList();

        foreach (var evaluator in caseEvaluators)
        {
            try
            {
                var result = await evaluator.EvaluateAsync(
                    messages, agentResponse, chatConfig, additionalContext, ct)
                    .ConfigureAwait(false);
                evalResults.Add(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                evalFailures.Add(new EvaluatorFailure(evaluator.GetType().Name, ex.Message));
            }
        }

        var evalDuration = DateTimeOffset.UtcNow - evalStart;
        var mergedResult = MergeResults(evalResults);

        return new ReportCase(
            caseName,
            mergedResult,
            evalFailures,
            taskDuration,
            evalDuration,
            taskDuration + evalDuration);
    }

    private static EvaluationResult MergeResults(List<EvaluationResult> results)
    {
        var merged = new EvaluationResult();
        foreach (var result in results)
        foreach (var (name, metric) in result.Metrics)
            merged.Metrics[name] = metric;
        return merged;
    }

    private static bool IsInfrastructureError(Exception ex)
    {
        var msg = ex.Message.ToLowerInvariant();
        return msg.Contains("429") || msg.Contains("503") ||
               msg.Contains("rate limit") || msg.Contains("too many requests");
    }
}

/// <summary>Minimal agent interface for RunEvals (avoids coupling to full Agent class).</summary>
public interface IAgent
{
    Task<ChatResponse> RunAsync(AgentRunConfig config, CancellationToken ct = default);
}
