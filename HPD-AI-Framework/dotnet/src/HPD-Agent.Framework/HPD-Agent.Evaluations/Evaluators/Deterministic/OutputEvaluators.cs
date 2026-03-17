// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Contexts;

namespace HPD.Agent.Evaluations.Evaluators.Deterministic;

/// <summary>BooleanMetric — response text contains the specified substring.</summary>
public sealed class OutputContainsEvaluator(string value) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Output Contains"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Output Contains");
        metric.Value = (modelResponse.Text ?? string.Empty).Contains(value, StringComparison.Ordinal);
        metric.Reason = metric.Value == true
            ? $"Output contains '{value}'."
            : $"Output does not contain '{value}'.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — regex match on output text.</summary>
public sealed class OutputMatchesRegexEvaluator(string pattern) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Output Matches Regex"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Output Matches Regex");
        metric.Value = Regex.IsMatch(modelResponse.Text ?? string.Empty, pattern);
        metric.Reason = metric.Value == true
            ? $"Output matches regex '{pattern}'."
            : $"Output does not match regex '{pattern}'.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — exact string match on output text.</summary>
public sealed class OutputEqualsEvaluator(string value) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Output Equals"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Output Equals");
        metric.Value = (modelResponse.Text ?? string.Empty) == value;
        metric.Reason = metric.Value == true ? "Output matches expected value." : "Output does not match expected value.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — exact match with the ground truth from GroundTruthContext.</summary>
public sealed class EqualsGroundTruthEvaluator : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Equals Ground Truth"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Equals Ground Truth");
        var ctx = additionalContext?.OfType<GroundTruthContext>().FirstOrDefault();

        if (ctx is null)
        {
            metric.AddDiagnostics(EvaluationDiagnostic.Error("GroundTruthContext is required."));
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        metric.Value = (modelResponse.Text ?? string.Empty) == ctx.Expected;
        metric.Reason = metric.Value == true ? "Output matches ground truth." : "Output does not match ground truth.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>NumericMetric 0–1 — fraction of keywords present in output.</summary>
public sealed class KeywordCoverageEvaluator(string[] keywords) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Keyword Coverage"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new NumericMetric("Keyword Coverage");
        if (keywords.Length == 0)
        {
            metric.Value = 1.0;
            metric.Reason = "No keywords specified.";
            metric.MarkAsHpdBuiltIn();
            return ValueTask.FromResult(new EvaluationResult(metric));
        }

        var text = modelResponse.Text ?? string.Empty;
        int found = keywords.Count(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        metric.Value = Math.Round((double)found / keywords.Length, 2);
        metric.Reason = $"{found}/{keywords.Length} keywords found.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>NumericMetric 0–1 — character-level similarity (Dice coefficient).</summary>
public sealed class ContentSimilarityEvaluator(string expected) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Content Similarity"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new NumericMetric("Content Similarity");
        var actual = modelResponse.Text ?? string.Empty;

        metric.Value = Math.Round(DiceSimilarity(expected, actual), 2);
        metric.Reason = $"Character-level similarity: {metric.Value:P0}.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }

    private static double DiceSimilarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var bigramsA = GetBigrams(a);
        var bigramsB = GetBigrams(b);
        int intersection = bigramsA.Intersect(bigramsB).Count();
        return (2.0 * intersection) / (bigramsA.Count + bigramsB.Count);
    }

    private static List<string> GetBigrams(string s) =>
        Enumerable.Range(0, Math.Max(0, s.Length - 1))
            .Select(i => s.Substring(i, 2))
            .ToList();
}
