// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations;

/// <summary>
/// Shared pass/fail interpretation for evaluation metrics.
/// Explicit evaluation interpretations take precedence over HPD's numeric fallback.
/// </summary>
internal static class EvaluationMetricSemantics
{
    internal static bool IsPassing(EvaluationMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);

        if (metric.Interpretation is { } interpretation)
            return !interpretation.Failed;

        return metric switch
        {
            BooleanMetric boolean => boolean.Value == true,
            NumericMetric numeric => numeric.Value is { } value && double.IsFinite(value) && value >= 0.5,
            _ => false,
        };
    }

    internal static bool IsPassing(EvaluationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Metrics.Count == 0)
            return false;

        foreach (var metric in result.Metrics.Values)
        {
            if (!IsPassing(metric))
                return false;
        }

        return true;
    }
}
