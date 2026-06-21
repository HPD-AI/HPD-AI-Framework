// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent;

namespace HPD.Agent.Evaluations.Integration;

/// <summary>
/// Typed helpers for evaluation-specific AgentRunConfig fields.
/// The backing AgentRunConfig properties are object-typed so HPD-Agent does not
/// need to reference HPD-Agent.Evaluations.
/// </summary>
public static class AgentRunConfigEvalExtensions
{
    public static AgentRunConfig WithAdditionalEvaluators(
        this AgentRunConfig config,
        params IEvaluator[] evaluators)
    {
        config.AdditionalEvaluators = evaluators;
        return config;
    }

    public static AgentRunConfig WithEvaluatorSamplingOverride(
        this AgentRunConfig config,
        double samplingRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(samplingRate, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(samplingRate, 1.0);

        config.EvaluatorSamplingOverride = samplingRate;
        return config;
    }

    public static AgentRunConfig WithEvalJudgeConfigOverride(
        this AgentRunConfig config,
        EvalJudgeConfig judgeConfig)
    {
        config.EvalJudgeConfigOverride = judgeConfig;
        return config;
    }

    internal static IReadOnlyList<IEvaluator> GetAdditionalEvaluators(this AgentRunConfig config)
        => config.AdditionalEvaluators?.OfType<IEvaluator>().ToList() ?? [];

    internal static EvalJudgeConfig? GetEvalJudgeConfigOverride(this AgentRunConfig config)
        => config.EvalJudgeConfigOverride as EvalJudgeConfig;
}
