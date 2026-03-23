// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations.Evaluators.Deterministic;

/// <summary>BooleanMetric — turn completed within the specified time limit.</summary>
public sealed class MaxDurationEvaluator(double maxSeconds) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Max Duration"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Max Duration");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        metric.Value = ctx.Duration.TotalSeconds <= maxSeconds;
        metric.Reason = $"Turn duration: {ctx.Duration.TotalSeconds:F1}s (limit: {maxSeconds}s).";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — turn used ≤ N LLM calls.</summary>
public sealed class MaxIterationsEvaluator(int maxIterations) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Max Iterations"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Max Iterations");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        metric.Value = ctx.IterationCount <= maxIterations;
        metric.Reason = $"Iteration count: {ctx.IterationCount} (limit: {maxIterations}).";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — total tokens ≤ N.</summary>
public sealed class MaxTokensEvaluator(int maxTokens) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Max Tokens"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Max Tokens");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        long total = (ctx.TurnUsage?.TotalTokenCount ?? 0);
        metric.Value = total <= maxTokens;
        metric.Reason = $"Total tokens: {total} (limit: {maxTokens}).";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — input tokens ≤ N.</summary>
public sealed class MaxInputTokensEvaluator(int maxTokens) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Max Input Tokens"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Max Input Tokens");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        long input = ctx.TurnUsage?.InputTokenCount ?? 0;
        metric.Value = input <= maxTokens;
        metric.Reason = $"Input tokens: {input} (limit: {maxTokens}).";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — output tokens ≤ N.</summary>
public sealed class MaxOutputTokensEvaluator(int maxTokens) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Max Output Tokens"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Max Output Tokens");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        long output = ctx.TurnUsage?.OutputTokenCount ?? 0;
        metric.Value = output <= maxTokens;
        metric.Reason = $"Output tokens: {output} (limit: {maxTokens}).";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}
