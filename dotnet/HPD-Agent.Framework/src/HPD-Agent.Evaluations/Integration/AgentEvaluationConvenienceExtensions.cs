// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using HPD.Agent.Evaluations.Batch;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Convenience entry points for quick string-prompt evaluation runs.
/// </summary>
public static class AgentEvaluationConvenienceExtensions
{
    public static Task<EvaluationReport> EvaluateAsync(
        this HPD.Agent.Agent agent,
        IEnumerable<string> prompts,
        IReadOnlyList<IEvaluator> evaluators,
        RunEvalsOptions<string>? options = null,
        string? experimentName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(evaluators);

        var dataset = new Dataset<string>
        {
            Cases = prompts.Select((prompt, index) => new EvalCase<string>
            {
                Name = $"case-{index + 1}",
                Input = prompt,
            }).ToList(),
        };

        return RunEvals.ExecuteAsync(agent, dataset, evaluators, options, experimentName, ct);
    }

    public static Task<EvaluationReport> EvaluateAsync(
        this HPD.Agent.Agent agent,
        IEnumerable<(string Prompt, string? GroundTruth)> cases,
        IReadOnlyList<IEvaluator> evaluators,
        RunEvalsOptions<string>? options = null,
        string? experimentName = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(evaluators);

        var dataset = new Dataset<string>
        {
            Cases = cases.Select((evalCase, index) => new EvalCase<string>
            {
                Name = $"case-{index + 1}",
                Input = evalCase.Prompt,
                GroundTruth = evalCase.GroundTruth,
            }).ToList(),
        };

        return RunEvals.ExecuteAsync(agent, dataset, evaluators, options, experimentName, ct);
    }

    public static Task<EvaluationReport> CheckAsync(
        this HPD.Agent.Agent agent,
        string prompt,
        params IEvaluator[] evaluators)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(evaluators);

        return agent.EvaluateAsync(
            [prompt],
            evaluators,
            options: null,
            experimentName: null,
            ct: default);
    }

    public static Task<EvaluationReport> CheckAsync(
        this HPD.Agent.Agent agent,
        string prompt,
        CancellationToken ct,
        params IEvaluator[] evaluators)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(evaluators);

        return agent.EvaluateAsync(
            [prompt],
            evaluators,
            options: null,
            experimentName: null,
            ct: ct);
    }
}
