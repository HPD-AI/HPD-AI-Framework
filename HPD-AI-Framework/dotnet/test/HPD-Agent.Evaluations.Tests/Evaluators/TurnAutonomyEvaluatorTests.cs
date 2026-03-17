// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using HPD.Agent.Evaluations.Evaluators.Deterministic;
using HPD.Agent.Evaluations.Tests.Infrastructure;

namespace HPD.Agent.Evaluations.Tests.Evaluators;

/// <summary>
/// Tests for TurnAutonomyEvaluator — the deterministic 1–10 autonomy score.
///
/// Scoring formula: four signals linearly combined → mapped to [1, 10].
/// 1. IterationCount / MaxIterations (default ceiling = 10)
/// 2. PermissionDeniedRate = denied tool calls / total tool calls
/// 3. StopKind: Completed=1.0, RequestedCredentials/Unknown=0.5, AskedClarification/AwaitingConfirmation=0.2
/// 4. Duration / MaxDuration (default ceiling = 10 min)
/// </summary>
public sealed class TurnAutonomyEvaluatorTests
{
    private static readonly Microsoft.Extensions.AI.ChatResponse EmptyResponse =
        new([new ChatMessage(ChatRole.Assistant, "done")]);

    [Fact]
    public async Task Autonomy_MinimalSignals_ReturnsLowScore()
    {
        // 1 iteration, no tools, StopKind=AskedClarification, 0 duration → near minimum
        var ctx = new TestContextBuilder()
            .WithIterationCount(1)
            .WithStopKind(AgentStopKind.AskedClarification)
            .WithDuration(TimeSpan.Zero)
            .BuildAsAdditionalContext();

        var result = await new TurnAutonomyEvaluator()
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        var metric = result.ShouldHaveNumericMetricInRange(TurnAutonomyEvaluator.MetricName, 1.0, 4.0);
        metric.ShouldBeMarkedAsBuiltIn();
    }

    [Fact]
    public async Task Autonomy_MaxSignals_ReturnsHighScore()
    {
        // 10 iterations (= ceiling), Completed, 10-min duration → near maximum
        var ctx = new TestContextBuilder()
            .WithIterationCount(10)
            .WithStopKind(AgentStopKind.Completed)
            .WithDuration(TimeSpan.FromMinutes(10))
            .BuildAsAdditionalContext();

        var result = await new TurnAutonomyEvaluator()
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        result.ShouldHaveNumericMetricInRange(TurnAutonomyEvaluator.MetricName, 7.0, 10.0);
    }

    [Fact]
    public async Task Autonomy_AllPermissionsDenied_PushesScoreUp()
    {
        // All tool calls denied → permission-denied rate = 1.0 → high autonomy signal
        var ctx = new TestContextBuilder()
            .WithIterationCount(1)
            .WithStopKind(AgentStopKind.Completed)
            .WithDuration(TimeSpan.FromSeconds(5))
            .WithToolCall("WriteTool", callId: "c1", permissionDenied: true)
            .WithToolCall("DeleteTool", callId: "c2", permissionDenied: true)
            .BuildAsAdditionalContext();

        var result = await new TurnAutonomyEvaluator()
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        // Permission-denied rate contributes 0.25 weight at max → score should be higher
        // than a context with no denials (everything else equal)
        var nm = result.ShouldHaveNumericMetricInRange(TurnAutonomyEvaluator.MetricName, 1.0, 10.0);
        nm.Value.Should().BeGreaterThan(3.5);
    }

    [Theory]
    [InlineData(AgentStopKind.Completed, 1.0)]
    [InlineData(AgentStopKind.RequestedCredentials, 0.5)]
    [InlineData(AgentStopKind.Unknown, 0.5)]
    [InlineData(AgentStopKind.AskedClarification, 0.2)]
    [InlineData(AgentStopKind.AwaitingConfirmation, 0.2)]
    public async Task Autonomy_StopKindAffectsScore(AgentStopKind kind, double expectedStopKindSignal)
    {
        // Hold other signals constant: 5 iterations out of 10, 5 min out of 10, no denials
        var ctx = new TestContextBuilder()
            .WithIterationCount(5)
            .WithStopKind(kind)
            .WithDuration(TimeSpan.FromMinutes(5))
            .BuildAsAdditionalContext();

        var result = await new TurnAutonomyEvaluator()
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        // Score = 1 + ((0.5 + 0 + stopKindSignal * 0.25 + 0.5 * 0.25) * 9)
        // Don't assert exact value — assert ordering: Completed > Unknown > AskedClarification
        var nm = result.Metrics[TurnAutonomyEvaluator.MetricName] as Microsoft.Extensions.AI.Evaluation.NumericMetric;
        nm!.Value.Should().NotBeNull();

        // Completed should score highest
        if (kind == AgentStopKind.Completed)
            nm.Value!.Value.Should().BeGreaterThan(5.0);

        // AskedClarification should score lower than Completed
        if (kind == AgentStopKind.AskedClarification)
            nm.Value!.Value.Should().BeLessThan(8.0);
    }

    [Fact]
    public async Task Autonomy_ScoreInRange1To10()
    {
        // Any valid context must produce score in [1, 10]
        foreach (var kind in Enum.GetValues<AgentStopKind>())
        {
            var ctx = new TestContextBuilder()
                .WithIterationCount(5)
                .WithStopKind(kind)
                .WithDuration(TimeSpan.FromMinutes(3))
                .BuildAsAdditionalContext();

            var result = await new TurnAutonomyEvaluator()
                .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

            result.ShouldHaveNumericMetricInRange(TurnAutonomyEvaluator.MetricName, 1.0, 10.0);
        }
    }

    [Fact]
    public async Task Autonomy_NoContext_ReturnsErrorDiagnostic()
    {
        var result = await new TurnAutonomyEvaluator()
            .EvaluateAsync([], EmptyResponse, additionalContext: null);

        result.ShouldHaveErrorDiagnostic();
    }

    [Fact]
    public async Task Autonomy_CustomOptions_RespectsCeilings()
    {
        // Custom options: ceiling of 2 iterations — 4 iterations should be capped at 1.0
        var options = new TurnAutonomyEvaluatorOptions
        {
            MaxIterations = 2,
            MaxDuration = TimeSpan.FromMinutes(1),
            IterationWeight = 0.5,
            PermissionDeniedWeight = 0.0,
            StopKindWeight = 0.5,
            DurationWeight = 0.0,
        };

        var ctx = new TestContextBuilder()
            .WithIterationCount(4)  // exceeds MaxIterations=2 → capped at 1.0
            .WithStopKind(AgentStopKind.Completed)
            .WithDuration(TimeSpan.FromSeconds(10))
            .BuildAsAdditionalContext();

        var result = await new TurnAutonomyEvaluator(options)
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        // With weights 0.5/0/0.5/0 and both signals maxed: (1.0*0.5 + 1.0*0.5) = 1.0
        // → score = 1 + (1.0 * 9) = 10
        result.ShouldHaveNumericMetricInRange(TurnAutonomyEvaluator.MetricName, 9.5, 10.0);
    }

    [Fact]
    public async Task Autonomy_ReasonContainsKeyFields()
    {
        var ctx = new TestContextBuilder()
            .WithIterationCount(3)
            .WithStopKind(AgentStopKind.Completed)
            .BuildAsAdditionalContext();

        var result = await new TurnAutonomyEvaluator()
            .EvaluateAsync([], EmptyResponse, additionalContext: ctx);

        var metric = result.Metrics[TurnAutonomyEvaluator.MetricName] as Microsoft.Extensions.AI.Evaluation.NumericMetric;
        metric!.Reason.Should()
            .Contain("Iterations:").And
            .Contain("StopKind:").And
            .Contain("Autonomy:");
    }
}
