// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Tests.Infrastructure;

/// <summary>
/// Assertion helpers for EvaluationResult, EvaluationMetric, etc.
/// Reduces boilerplate in evaluator tests.
/// </summary>
internal static class EvalAssert
{
    /// <summary>Asserts the first metric in the result is a BooleanMetric with the given value.</summary>
    public static BooleanMetric ShouldHaveBooleanMetric(this EvaluationResult result,
        string metricName, bool expectedValue)
    {
        result.Metrics.Should().ContainKey(metricName,
            $"EvaluationResult should contain metric '{metricName}'");

        var metric = result.Metrics[metricName];
        metric.Should().BeOfType<BooleanMetric>($"metric '{metricName}' should be a BooleanMetric");

        var bm = (BooleanMetric)metric;
        bm.Value.Should().Be(expectedValue,
            $"metric '{metricName}' should be {expectedValue}");
        return bm;
    }

    /// <summary>Asserts the result has a NumericMetric within the given range.</summary>
    public static NumericMetric ShouldHaveNumericMetricInRange(this EvaluationResult result,
        string metricName, double min, double max)
    {
        result.Metrics.Should().ContainKey(metricName);
        var metric = result.Metrics[metricName];
        metric.Should().BeOfType<NumericMetric>();
        var nm = (NumericMetric)metric;
        nm.Value.Should().NotBeNull($"metric '{metricName}' should have a value");
        nm.Value!.Value.Should().BeInRange(min, max,
            $"metric '{metricName}' value {nm.Value.Value} should be in [{min}, {max}]");
        return nm;
    }

    /// <summary>Asserts the result has error diagnostics (no TurnEvaluationContext case).</summary>
    public static void ShouldHaveErrorDiagnostic(this EvaluationResult result)
    {
        var hasError = result.Metrics.Values.Any(m =>
            m.Diagnostics?.Any(d => d.Severity == EvaluationDiagnosticSeverity.Error) == true);
        hasError.Should().BeTrue("result should contain at least one error diagnostic");
    }

    /// <summary>Asserts the metric has the built-in metadata marker.</summary>
    public static void ShouldBeMarkedAsBuiltIn(this EvaluationMetric metric)
    {
        metric.Metadata.Should().ContainKey("built-in-eval",
            "evaluator should mark the metric as built-in");
        metric.Metadata["built-in-eval"].Should().Be("True");
    }
}
