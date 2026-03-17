// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Evaluators;

/// <summary>
/// Tests for TaskOracleEvaluator — the ground-truth oracle base class.
///
/// Key behaviors:
/// - Without TurnEvaluationContextWrapper in additionalContext → error diagnostic
/// - When RunOracleAsync returns OracleResult.Pass() → BooleanMetric true
/// - When RunOracleAsync returns OracleResult.Fail(reason) → BooleanMetric false with reason
/// - When RunOracleAsync throws (non-cancellation) → error diagnostic, metric not set
/// - OracleMetricName override → metric uses custom name
/// </summary>
public sealed class TaskOracleEvaluatorTests
{
    private static readonly ChatResponse AnyResponse =
        new([new ChatMessage(ChatRole.Assistant, "output")]);

    // ── Concrete oracle implementations for testing ───────────────────────────

    /// <summary>Oracle that always passes.</summary>
    private sealed class AlwaysPassOracle : TaskOracleEvaluator
    {
        protected override Task<OracleResult> RunOracleAsync(
            TurnEvaluationContext ctx, CancellationToken ct)
            => Task.FromResult(OracleResult.Pass("all good"));
    }

    /// <summary>Oracle that always fails.</summary>
    private sealed class AlwaysFailOracle : TaskOracleEvaluator
    {
        protected override Task<OracleResult> RunOracleAsync(
            TurnEvaluationContext ctx, CancellationToken ct)
            => Task.FromResult(OracleResult.Fail("expected different output"));
    }

    /// <summary>Oracle that throws.</summary>
    private sealed class ThrowingOracle : TaskOracleEvaluator
    {
        protected override Task<OracleResult> RunOracleAsync(
            TurnEvaluationContext ctx, CancellationToken ct)
            => throw new InvalidOperationException("database connection refused");
    }

    /// <summary>Oracle with custom metric name.</summary>
    private sealed class SqlOracle : TaskOracleEvaluator
    {
        protected override string OracleMetricName => "SQL Execution";

        protected override Task<OracleResult> RunOracleAsync(
            TurnEvaluationContext ctx, CancellationToken ct)
        {
            bool passed = ctx.OutputText.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(passed
                ? OracleResult.Pass()
                : OracleResult.Fail($"Expected SQL SELECT but got: {ctx.OutputText}"));
        }
    }

    /// <summary>Oracle that reads from TurnEvaluationContext.GroundTruth.</summary>
    private sealed class GroundTruthOracle : TaskOracleEvaluator
    {
        protected override Task<OracleResult> RunOracleAsync(
            TurnEvaluationContext ctx, CancellationToken ct)
        {
            if (ctx.GroundTruth is null)
                return Task.FromResult(OracleResult.Fail("No ground truth provided"));

            bool passed = ctx.OutputText.Trim() == ctx.GroundTruth.Trim();
            return Task.FromResult(passed
                ? OracleResult.Pass()
                : OracleResult.Fail($"Expected '{ctx.GroundTruth}' but got '{ctx.OutputText}'"));
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Oracle_NoContext_ReturnsErrorDiagnostic()
    {
        var result = await new AlwaysPassOracle()
            .EvaluateAsync([], AnyResponse, additionalContext: null);

        result.ShouldHaveErrorDiagnostic();
        var metric = result.Metrics["Oracle"] as BooleanMetric;
        metric!.Value.Should().BeNull("metric should not be set on error path");
    }

    [Fact]
    public async Task Oracle_Pass_ReturnsTrueWithReason()
    {
        var ctx = new TestContextBuilder().BuildAsAdditionalContext();

        var result = await new AlwaysPassOracle()
            .EvaluateAsync([], AnyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Oracle", true)
            .ShouldBeMarkedAsBuiltIn();

        var metric = result.Metrics["Oracle"] as BooleanMetric;
        metric!.Reason.Should().Be("all good");
    }

    [Fact]
    public async Task Oracle_Fail_ReturnsFalseWithReason()
    {
        var ctx = new TestContextBuilder().BuildAsAdditionalContext();

        var result = await new AlwaysFailOracle()
            .EvaluateAsync([], AnyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Oracle", false);
        var metric = result.Metrics["Oracle"] as BooleanMetric;
        metric!.Reason.Should().Be("expected different output");
    }

    [Fact]
    public async Task Oracle_Throws_ReturnsErrorDiagnostic()
    {
        var ctx = new TestContextBuilder().BuildAsAdditionalContext();

        // Should not propagate the exception — wraps in error diagnostic
        var result = await new ThrowingOracle()
            .EvaluateAsync([], AnyResponse, additionalContext: ctx);

        result.ShouldHaveErrorDiagnostic();
        var errorMetric = result.Metrics["Oracle"] as BooleanMetric;
        errorMetric!.Diagnostics.Should().Contain(d =>
            d.Severity == EvaluationDiagnosticSeverity.Error &&
            d.Message.Contains("database connection refused"));
    }

    [Fact]
    public async Task Oracle_CustomMetricName_UsesCorrectKey()
    {
        var ctx = new TestContextBuilder()
            .WithOutputText("SELECT * FROM users")
            .BuildAsAdditionalContext();

        var result = await new SqlOracle()
            .EvaluateAsync([], AnyResponse, additionalContext: ctx);

        result.Metrics.Should().ContainKey("SQL Execution",
            "custom OracleMetricName should be used");
        result.ShouldHaveBooleanMetric("SQL Execution", true);
    }

    [Fact]
    public async Task Oracle_CustomMetricName_FailsOnWrongOutput()
    {
        var ctx = new TestContextBuilder()
            .WithOutputText("DROP TABLE users")
            .BuildAsAdditionalContext();

        var result = await new SqlOracle()
            .EvaluateAsync([], AnyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("SQL Execution", false);
        var metric = result.Metrics["SQL Execution"] as BooleanMetric;
        metric!.Reason.Should().Contain("DROP TABLE users");
    }

    [Fact]
    public async Task Oracle_AccessesTurnContext_GroundTruth()
    {
        var ctx = new TestContextBuilder()
            .WithOutputText("Paris")
            .WithGroundTruth("Paris")
            .BuildAsAdditionalContext();

        var result = await new GroundTruthOracle()
            .EvaluateAsync([], AnyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Oracle", true);
    }

    [Fact]
    public async Task Oracle_AccessesTurnContext_GroundTruthMismatch()
    {
        var ctx = new TestContextBuilder()
            .WithOutputText("London")
            .WithGroundTruth("Paris")
            .BuildAsAdditionalContext();

        var result = await new GroundTruthOracle()
            .EvaluateAsync([], AnyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Oracle", false);
        var metric = result.Metrics["Oracle"] as BooleanMetric;
        metric!.Reason.Should().Contain("Paris").And.Contain("London");
    }

    [Fact]
    public void OracleResult_PassFactory_IsCorrect()
    {
        var pass = OracleResult.Pass("success");
        pass.Passed.Should().BeTrue();
        pass.Reason.Should().Be("success");

        var passNoReason = OracleResult.Pass();
        passNoReason.Passed.Should().BeTrue();
        passNoReason.Reason.Should().BeNull();
    }

    [Fact]
    public void OracleResult_FailFactory_IsCorrect()
    {
        var fail = OracleResult.Fail("wrong answer");
        fail.Passed.Should().BeFalse();
        fail.Reason.Should().Be("wrong answer");
    }
}
