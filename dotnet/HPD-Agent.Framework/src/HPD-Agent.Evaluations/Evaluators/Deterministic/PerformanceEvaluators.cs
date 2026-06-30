// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

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

/// <summary>NumericMetric — reports turn latency in seconds.</summary>
public sealed class LatencyEvaluator : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Latency"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new NumericMetric("Latency");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        metric.Value = Math.Round(ctx.Duration.TotalSeconds, 4);
        metric.Reason = $"Turn latency: {ctx.Duration.TotalSeconds:F4}s.";
        metric.AddOrUpdateMetadata("latency-ms", ctx.Duration.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — estimated turn cost is within the configured limit.</summary>
public sealed class MaxCostEvaluator(double maxCostUsd) : HpdDeterministicEvaluatorBase
{
    private static readonly string[] CostMetricKeys =
    [
        "cost_usd",
        "turn_cost_usd",
        "estimated_cost_usd",
        "cost"
    ];

    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Max Cost"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Max Cost");
        var ctx = additionalContext?.OfType<TurnEvaluationContextWrapper>().FirstOrDefault()?.Context;

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("TurnEvaluationContext not available."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        if (!TryGetCost(ctx, out var cost, out var key))
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Warning(
                $"No cost metric found. Expected one of: {string.Join(", ", CostMetricKeys)}."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        metric.Value = cost <= maxCostUsd;
        metric.Reason = $"Estimated cost: ${cost:F6} from '{key}' (limit: ${maxCostUsd:F6}).";
        metric.AddOrUpdateMetadata("cost-usd", cost.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }

    private static bool TryGetCost(TurnEvaluationContext ctx, out double cost, out string key)
    {
        foreach (var candidate in CostMetricKeys)
        {
            if (ctx.Metrics.TryGetValue(candidate, out cost))
            {
                key = candidate;
                return true;
            }
        }

        cost = 0;
        key = string.Empty;
        return false;
    }
}
