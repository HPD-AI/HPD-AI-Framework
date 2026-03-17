// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Contexts;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Evaluators.LlmJudge;

namespace HPD.Agent.Evaluations.Evaluators.Composite;

// ── WeightedScoreEvaluator ───────────────────────────────────────────────────

/// <summary>
/// Computes a weighted average of sub-evaluator NumericMetric scores into a single
/// composite score. Weights are auto-normalized if they don't sum to 1.
/// Sub-evaluators returning Inconclusive (null Value) or throwing (exception isolation
/// path produces base EvaluationMetric, not NumericMetric) are excluded from the average
/// and their weights redistributed. If ALL sub-evaluators are inconclusive, the composite
/// metric is also Inconclusive.
/// </summary>
public sealed class WeightedScoreEvaluator : HpdEvaluatorBase
{
    public const string MetricName = "Weighted Score";

    private readonly IReadOnlyList<(IEvaluator Evaluator, double Weight)> _entries;

    public WeightedScoreEvaluator(IReadOnlyList<(IEvaluator Evaluator, double Weight)> evaluators)
    {
        _entries = evaluators;
    }

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    public override async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var composite = new NumericMetric(MetricName);
        var result = new EvaluationResult(composite);

        if (_entries.Count == 0)
        {
            composite.AddDiagnostics(EvaluationDiagnostic.Warning("No sub-evaluators registered."));
            return result;
        }

        // Run all sub-evaluators concurrently
        var tasks = _entries.Select(entry =>
            RunSafeAsync(entry.Evaluator, messages, modelResponse, chatConfiguration,
                additionalContext, cancellationToken)).ToList();

        var subResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        // Collect valid numeric scores
        var validEntries = new List<(double score, double weight, string name)>();
        for (int i = 0; i < _entries.Count; i++)
        {
            var (evaluator, weight) = _entries[i];
            var subResult = subResults[i];
            var metricName = evaluator.EvaluationMetricNames.FirstOrDefault() ?? "?";

            if (subResult is null)
            {
                composite.AddDiagnostics(EvaluationDiagnostic.Warning(
                    $"Sub-evaluator '{metricName}' failed (exception). Excluded from weighted average."));
                continue;
            }

            var firstMetric = subResult.Metrics.Values.FirstOrDefault();
            if (firstMetric is not NumericMetric nm)
            {
                composite.AddDiagnostics(EvaluationDiagnostic.Warning(
                    $"Sub-evaluator '{metricName}' did not return NumericMetric. Excluded."));
                continue;
            }

            if (!nm.Value.HasValue)
            {
                composite.AddDiagnostics(EvaluationDiagnostic.Informational(
                    $"Sub-evaluator '{metricName}' returned Inconclusive. Excluded from average."));
                continue;
            }

            validEntries.Add((nm.Value.Value, weight, metricName));
        }

        if (validEntries.Count == 0)
        {
            composite.AddDiagnostics(EvaluationDiagnostic.Warning(
                "All sub-evaluators were inconclusive or failed. Composite score is Inconclusive."));
            return result;
        }

        // Normalize weights among valid entries
        double totalWeight = validEntries.Sum(e => e.weight);
        if (totalWeight <= 0) totalWeight = 1.0;

        double weightedSum = validEntries.Sum(e => e.score * (e.weight / totalWeight));
        composite.Value = Math.Round(weightedSum, 4);
        composite.Reason = string.Join(", ", validEntries.Select(e =>
            $"{e.name}: {e.score:F2} (w={e.weight / totalWeight:F2})"));
        composite.MarkAsHpdBuiltIn();
        return result;
    }

    private static async Task<EvaluationResult?> RunSafeAsync(
        IEvaluator evaluator,
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken ct)
    {
        try
        {
            return await evaluator.EvaluateAsync(
                messages, modelResponse, chatConfiguration, additionalContext, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}

// ── ThresholdGate ────────────────────────────────────────────────────────────

/// <summary>
/// Wraps any NumericMetric evaluator and converts its score to a BooleanMetric:
/// passes if score >= threshold. Propagates the inner evaluator's Reason.
/// Useful for turning continuous scores into CI gates.
/// </summary>
public sealed class ThresholdGate : HpdEvaluatorBase
{
    private readonly IEvaluator _inner;
    private readonly double _threshold;
    private readonly string _metricName;

    public ThresholdGate(IEvaluator inner, double threshold)
    {
        _inner = inner;
        _threshold = threshold;
        _metricName = $"{inner.EvaluationMetricNames.FirstOrDefault() ?? "Score"} >= {threshold:F2}";
    }

    public override IReadOnlyCollection<string> EvaluationMetricNames => [_metricName];

    public override async ValueTask<EvaluationResult> EvaluateAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null,
        IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        var gate = new BooleanMetric(_metricName);
        var result = new EvaluationResult(gate);

        EvaluationResult innerResult;
        try
        {
            innerResult = await _inner.EvaluateAsync(
                messages, modelResponse, chatConfiguration, additionalContext, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            gate.AddDiagnostics(EvaluationDiagnostic.Error(
                $"Inner evaluator threw: {ex.Message}"));
            return result;
        }

        var innerMetric = innerResult.Metrics.Values.FirstOrDefault();
        if (innerMetric is not NumericMetric nm || !nm.Value.HasValue)
        {
            gate.AddDiagnostics(EvaluationDiagnostic.Warning(
                "Inner evaluator returned Inconclusive — gate result is Inconclusive."));
            return result;
        }

        gate.Value = nm.Value.Value >= _threshold;
        gate.Reason = $"Score {nm.Value.Value:F4} {(gate.Value == true ? ">=" : "<")} threshold {_threshold:F2}. {nm.Reason}";
        gate.MarkAsHpdBuiltIn();
        return result;
    }
}

// ── SemanticFieldEqualityEvaluator ───────────────────────────────────────────

/// <summary>
/// LLM-as-judge: checks whether a specific JSON field in the structured output
/// is semantically equivalent to the ground truth (0–1 NumericMetric).
/// Used for evaluating structured output where exact string matching is too strict.
/// Default policy: TrackTrend.
/// </summary>
public sealed class SemanticFieldEqualityEvaluator : HpdLlmJudgeEvaluatorBase
{
    private readonly string _fieldName;
    public string MetricName => $"Semantic Field Equality ({_fieldName})";

    public SemanticFieldEqualityEvaluator(string fieldName)
        => _fieldName = fieldName;

    public override IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    protected override EvaluationResult CreateEmptyResult()
        => new(new NumericMetric(MetricName));

    protected override List<ChatMessage> BuildJudgePrompt(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext)
    {
        var groundTruth = additionalContext?.OfType<GroundTruthContext>().FirstOrDefault()?.Expected
            ?? "(not provided)";

        // Try to extract the field from the model response JSON
        string fieldValue = "(could not extract)";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(modelResponse.Text ?? "{}");
            if (doc.RootElement.TryGetProperty(_fieldName, out var prop))
                fieldValue = prop.ToString();
        }
        catch { }

        return
        [
            new(ChatRole.System,
                $"Rate the semantic similarity between the predicted value for field '{_fieldName}' " +
                "and the expected ground truth on a scale of 0 to 1. " +
                "0 = completely different meaning, 1 = semantically identical. " +
                "Think in <S0>, explain in <S1>, score in <S2>."),
            new(ChatRole.User,
                $"Expected: {groundTruth}\nPredicted field '{_fieldName}': {fieldValue}"),
        ];
    }

    protected override void ParseJudgeResponse(
        string responseText, EvaluationResult result, ChatResponse judgeResponse, TimeSpan duration)
    {
        var (_, reason, score) = TagParser.Parse(responseText);
        var metric = (NumericMetric)result.Metrics.Values.First();
        if (TagParser.TryParseDouble(score, out var v))
            metric.Value = Math.Clamp(v, 0.0, 1.0);
        metric.Reason = reason;
        metric.AddOrUpdateChatMetadata(judgeResponse, duration);
    }
}
