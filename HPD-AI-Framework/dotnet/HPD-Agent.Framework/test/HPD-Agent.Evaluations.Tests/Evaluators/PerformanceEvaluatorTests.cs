// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Evaluators;

/// <summary>
/// Tests for performance budget evaluators:
/// - MaxDurationEvaluator
/// - MaxIterationsEvaluator
/// - MaxTokensEvaluator
/// - MaxInputTokensEvaluator
/// - MaxOutputTokensEvaluator
/// </summary>
public sealed class PerformanceEvaluatorTests
{
    private static readonly ChatResponse EmptyResponse =
        new([new ChatMessage(ChatRole.Assistant, "ok")]);

    // ── MaxDurationEvaluator ──────────────────────────────────────────────────

    [Fact]
    public async Task MaxDuration_WithinLimit_ReturnsTrue()
    {
        var ctx = new TestContextBuilder()
            .WithDuration(TimeSpan.FromSeconds(5))
            .BuildAsAdditionalContext();

        var result = await new MaxDurationEvaluator(maxSeconds: 10)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Max Duration", true)
            .ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task MaxDuration_ExceedsLimit_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithDuration(TimeSpan.FromSeconds(15))
            .BuildAsAdditionalContext();

        var result = await new MaxDurationEvaluator(maxSeconds: 10)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Max Duration", false);
    }

    [Fact]
    public async Task MaxDuration_ExactlyAtLimit_ReturnsTrue()
    {
        // Boundary condition: exactly at limit is a pass (<=)
        var ctx = new TestContextBuilder()
            .WithDuration(TimeSpan.FromSeconds(10))
            .BuildAsAdditionalContext();

        var result = await new MaxDurationEvaluator(maxSeconds: 10)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Max Duration", true);
    }

    [Fact]
    public async Task MaxDuration_NoContext_ReturnsErrorDiagnostic()
    {
        var result = await new MaxDurationEvaluator(maxSeconds: 10)
            .EvaluateAsync([], EmptyResponse, additionalContext: null);

        result.ShouldHaveErrorDiagnostic();
    }

    [Fact]
    public async Task MaxDuration_ReasonContainsDurationAndLimit()
    {
        var ctx = new TestContextBuilder()
            .WithDuration(TimeSpan.FromSeconds(7))
            .BuildAsAdditionalContext();

        var result = await new MaxDurationEvaluator(maxSeconds: 10)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        var metric = result.Metrics["Max Duration"] as BooleanMetric;
        metric!.Reason.Should().Contain("10");
    }

    // ── MaxIterationsEvaluator ────────────────────────────────────────────────

    [Fact]
    public async Task MaxIterations_WithinLimit_ReturnsTrue()
    {
        var ctx = new TestContextBuilder()
            .WithIterationCount(3)
            .BuildAsAdditionalContext();

        var result = await new MaxIterationsEvaluator(maxIterations: 5)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Max Iterations", true);
    }

    [Fact]
    public async Task MaxIterations_ExceedsLimit_ReturnsFalse()
    {
        var ctx = new TestContextBuilder()
            .WithIterationCount(10)
            .BuildAsAdditionalContext();

        var result = await new MaxIterationsEvaluator(maxIterations: 5)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Max Iterations", false);
    }

    [Fact]
    public async Task MaxIterations_ExactlyAtLimit_ReturnsTrue()
    {
        var ctx = new TestContextBuilder()
            .WithIterationCount(5)
            .BuildAsAdditionalContext();

        var result = await new MaxIterationsEvaluator(maxIterations: 5)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Max Iterations", true);
    }

    // ── MaxTokensEvaluator ────────────────────────────────────────────────────

    [Fact]
    public async Task MaxTokens_NoUsageData_TreatedAsZero_ReturnsTrue()
    {
        // TurnUsage is null → 0 total tokens, which is always ≤ any positive limit
        var ctx = new TestContextBuilder().BuildAsAdditionalContext(); // no usage set

        var result = await new MaxTokensEvaluator(maxTokens: 1000)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveBooleanMetric("Max Tokens", true);
    }

    [Fact]
    public async Task MaxTokens_NoContext_ReturnsErrorDiagnostic()
    {
        var result = await new MaxTokensEvaluator(maxTokens: 1000)
            .EvaluateAsync([], EmptyResponse, additionalContext: null);

        result.ShouldHaveErrorDiagnostic();
    }
}
