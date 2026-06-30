// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using FluentAssertions;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Batch;

namespace HPD.Agent.Evaluations.Tests.Batch;

public sealed class ReportAnalyzerTests
{
    [Fact]
    public void ConfusionMatrixAnalyzer_BinaryClassification_ComputesCorrectMatrix()
    {
        var report = new EvaluationReport("classification", [
            Case(BoolMetric("Predicted", true), BoolMetric("Expected", true)),
            Case(BoolMetric("Predicted", true), BoolMetric("Expected", false)),
            Case(BoolMetric("Predicted", false), BoolMetric("Expected", false)),
            Case(BoolMetric("Predicted", false), BoolMetric("Expected", true)),
        ]);

        var analysis = new ConfusionMatrixAnalyzer("Predicted", "Expected")
            .Evaluate(report)
            .Should().ContainSingle().Subject;

        analysis.Passed.Should().BeTrue();
        analysis.Details!["true_positive"].Should().Be(1);
        analysis.Details!["false_positive"].Should().Be(1);
        analysis.Details!["true_negative"].Should().Be(1);
        analysis.Details!["false_negative"].Should().Be(1);
        analysis.Details!["precision"].Should().Be(0.5);
        analysis.Details!["recall"].Should().Be(0.5);
        analysis.Details!["f1"].Should().Be(0.5);
    }

    [Fact]
    public void ConfusionMatrixAnalyzer_NumericMetrics_ThresholdedCorrectly()
    {
        var report = new EvaluationReport("numeric", [
            Case(NumMetric("Predicted", 0.9), NumMetric("Expected", 1.0)),
            Case(NumMetric("Predicted", 0.7), NumMetric("Expected", 0.0)),
            Case(NumMetric("Predicted", 0.2), NumMetric("Expected", 0.0)),
            Case(NumMetric("Predicted", 0.1), NumMetric("Expected", 1.0)),
        ]);

        var analysis = new ConfusionMatrixAnalyzer("Predicted", "Expected", threshold: 0.5)
            .Evaluate(report)
            .Should().ContainSingle().Subject;

        analysis.Details!["true_positive"].Should().Be(1);
        analysis.Details!["false_positive"].Should().Be(1);
        analysis.Details!["true_negative"].Should().Be(1);
        analysis.Details!["false_negative"].Should().Be(1);
    }

    [Fact]
    public void ConfusionMatrixAnalyzer_StringMetrics_MatchesPositiveLabel()
    {
        var report = new EvaluationReport("strings", [
            Case(StrMetric("Predicted", "unsafe"), StrMetric("Expected", "unsafe")),
            Case(StrMetric("Predicted", "unsafe"), StrMetric("Expected", "safe")),
            Case(StrMetric("Predicted", "safe"), StrMetric("Expected", "safe")),
            Case(StrMetric("Predicted", "safe"), StrMetric("Expected", "unsafe")),
        ]);

        var analysis = new ConfusionMatrixAnalyzer(
                "Predicted",
                "Expected",
                positiveLabel: "unsafe")
            .Evaluate(report)
            .Should().ContainSingle().Subject;

        analysis.Details!["true_positive"].Should().Be(1);
        analysis.Details!["false_positive"].Should().Be(1);
        analysis.Details!["true_negative"].Should().Be(1);
        analysis.Details!["false_negative"].Should().Be(1);
    }

    [Fact]
    public void PrecisionRecallAnalyzer_PerfectClassifier_AucIsOne()
    {
        var report = new EvaluationReport("perfect", [
            Case(NumMetric("Score", 0.95), BoolMetric("Expected", true)),
            Case(NumMetric("Score", 0.80), BoolMetric("Expected", true)),
            Case(NumMetric("Score", 0.20), BoolMetric("Expected", false)),
            Case(NumMetric("Score", 0.10), BoolMetric("Expected", false)),
        ]);

        var analysis = new PrecisionRecallAnalyzer("Score", "Expected")
            .Evaluate(report)
            .Should().ContainSingle().Subject;

        analysis.Passed.Should().BeTrue();
        analysis.Details!["auc"].Should().Be(1.0);
    }

    [Fact]
    public void PrecisionRecallAnalyzer_RandomClassifier_AucIsPositivePrevalence()
    {
        var report = new EvaluationReport("random", [
            Case(NumMetric("Score", 0.5), BoolMetric("Expected", true)),
            Case(NumMetric("Score", 0.5), BoolMetric("Expected", false)),
            Case(NumMetric("Score", 0.5), BoolMetric("Expected", true)),
            Case(NumMetric("Score", 0.5), BoolMetric("Expected", false)),
        ]);

        var analysis = new PrecisionRecallAnalyzer("Score", "Expected")
            .Evaluate(report)
            .Should().ContainSingle().Subject;

        analysis.Passed.Should().BeTrue();
        analysis.Details!["auc"].Should().Be(0.5);
    }

    [Fact]
    public void PrecisionRecallAnalyzer_NoPositiveLabels_ReturnsZeroAuc()
    {
        var report = new EvaluationReport("no-positive", [
            Case(NumMetric("Score", 0.9), BoolMetric("Expected", false)),
            Case(NumMetric("Score", 0.1), BoolMetric("Expected", false)),
        ]);

        var analysis = new PrecisionRecallAnalyzer("Score", "Expected")
            .Evaluate(report)
            .Should().ContainSingle().Subject;

        analysis.Passed.Should().BeTrue();
        analysis.Details!["auc"].Should().Be(0.0);
    }

    [Fact]
    public void PrecisionRecallAnalyzer_MissingMetrics_SkipsCases()
    {
        var report = new EvaluationReport("missing", [
            Case(NumMetric("Score", 0.9), BoolMetric("Expected", true)),
            Case(NumMetric("Score", 0.1)),
            Case(BoolMetric("Expected", false)),
        ]);

        var analysis = new PrecisionRecallAnalyzer("Score", "Expected")
            .Evaluate(report)
            .Should().ContainSingle().Subject;

        analysis.Passed.Should().BeTrue();
        analysis.Details!["total"].Should().Be(1);
        analysis.Details!["skipped"].Should().Be(2);
    }

    private static ReportCase Case(params EvaluationMetric[] metrics)
        => new(
            Name: null,
            ProviderKey: null,
            ModelId: null,
            ResponseModelId: null,
            EvaluationResult: new EvaluationResult(metrics),
            EvaluatorFailures: [],
            TaskDuration: TimeSpan.Zero,
            EvaluatorDuration: TimeSpan.Zero,
            TotalDuration: TimeSpan.Zero);

    private static BooleanMetric BoolMetric(string name, bool value)
        => new(name) { Value = value };

    private static NumericMetric NumMetric(string name, double value)
        => new(name) { Value = value };

    private static StringMetric StrMetric(string name, string value)
        => new(name) { Value = value };
}
