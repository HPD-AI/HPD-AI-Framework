// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent;

namespace HPD.Agent.Evaluations.Integration;

internal static class AgentRunConfigEvaluationAccess
{
    internal static IReadOnlyList<IEvaluator> GetAdditionalEvaluators(this AgentRunConfig config)
        => Get(config)?.AdditionalEvaluators ?? [];

    internal static EvaluationJudgeRunConfig? GetEvalJudgeConfigOverride(this AgentRunConfig config)
        => Get(config)?.Judge;

    internal static EvaluationRunConfig? Get(this AgentRunConfig config) =>
        config.Evaluations as EvaluationRunConfig;

    internal static EvaluationRunConfig Ensure(this AgentRunConfig config) =>
        config.Evaluations as EvaluationRunConfig ??
        (EvaluationRunConfig)(config.Evaluations = new EvaluationRunConfig());

    internal static void SuppressEvaluation(
        this AgentRunConfig config,
        EvaluationSuppressionReason reason) => Ensure(config).SuppressionReason = reason;

    internal static bool IsEvaluationSuppressed(this AgentRunConfig config)
    {
        var evaluation = Get(config);
        return evaluation is { Enabled: false } ||
            evaluation?.SuppressionReason is not null and not EvaluationSuppressionReason.None;
    }
}
