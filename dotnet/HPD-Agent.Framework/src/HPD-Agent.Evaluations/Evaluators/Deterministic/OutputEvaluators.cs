// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;
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

/// <summary>BooleanMetric — response text contains any expected substring.</summary>
public sealed class ContainsAnyEvaluator(params string[] values) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Contains Any"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Contains Any");
        var text = modelResponse.Text ?? string.Empty;
        var matched = values.FirstOrDefault(v => text.Contains(v, StringComparison.Ordinal));

        metric.Value = values.Length == 0 || matched is not null;
        metric.Reason = matched is not null
            ? $"Output contains '{matched}'."
            : values.Length == 0
                ? "No values specified."
                : $"Output contains none of [{string.Join(", ", values)}].";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — response text contains all expected substrings.</summary>
public sealed class ContainsAllEvaluator(params string[] values) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Contains All"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Contains All");
        var text = modelResponse.Text ?? string.Empty;
        var missing = values.Where(v => !text.Contains(v, StringComparison.Ordinal)).ToList();

        metric.Value = missing.Count == 0;
        metric.Reason = missing.Count == 0
            ? "Output contains all expected values."
            : $"Output is missing [{string.Join(", ", missing)}].";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — case-insensitive response substring check.</summary>
public sealed class CaseInsensitiveContainsEvaluator(string value) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Case-Insensitive Contains"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Case-Insensitive Contains");
        metric.Value = (modelResponse.Text ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);
        metric.Reason = metric.Value == true
            ? $"Output contains '{value}' ignoring case."
            : $"Output does not contain '{value}' ignoring case.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — response text starts with the expected prefix.</summary>
public sealed class StartsWithEvaluator(string value, bool ignoreCase = false) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Starts With"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Starts With");
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        metric.Value = (modelResponse.Text ?? string.Empty).StartsWith(value, comparison);
        metric.Reason = metric.Value == true
            ? $"Output starts with '{value}'."
            : $"Output does not start with '{value}'.";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric with word-count metadata — validates min/max/exact word count.</summary>
public sealed class WordCountEvaluator(int? min = null, int? max = null, int? exact = null)
    : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Word Count"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Word Count");
        var count = CountWords(modelResponse.Text ?? string.Empty);
        var passed = exact.HasValue
            ? count == exact.Value
            : (!min.HasValue || count >= min.Value) && (!max.HasValue || count <= max.Value);

        metric.Value = passed;
        metric.Reason = exact.HasValue
            ? $"Word count: {count} (expected exactly {exact.Value})."
            : $"Word count: {count} (min: {min?.ToString() ?? "none"}, max: {max?.ToString() ?? "none"}).";
        metric.AddOrUpdateMetadata("word-count", count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }

    private static int CountWords(string text) =>
        Regex.Matches(text, @"\b[\p{L}\p{N}][\p{L}\p{N}'-]*\b").Count;
}

/// <summary>NumericMetric 0-1 — normalized Levenshtein similarity to expected output.</summary>
public sealed class LevenshteinEvaluator(string expected) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Levenshtein Similarity"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new NumericMetric("Levenshtein Similarity");
        var actual = modelResponse.Text ?? string.Empty;
        var maxLength = Math.Max(expected.Length, actual.Length);
        var distance = Distance(expected, actual);
        metric.Value = maxLength == 0 ? 1.0 : Math.Round(1.0 - ((double)distance / maxLength), 4);
        metric.Reason = $"Levenshtein distance: {distance}; normalized similarity: {metric.Value:F4}.";
        metric.AddOrUpdateMetadata("levenshtein-distance", distance.ToString(System.Globalization.CultureInfo.InvariantCulture));
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }

    private static int Distance(string source, string target)
    {
        if (source.Length == 0) return target.Length;
        if (target.Length == 0) return source.Length;

        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];

        for (int j = 0; j <= target.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Length];
    }
}

/// <summary>BooleanMetric — detects common refusal language in the response.</summary>
public sealed class RefusalEvaluator : HpdDeterministicEvaluatorBase
{
    private static readonly string[] RefusalPhrases =
    [
        "i can't",
        "i cannot",
        "i'm unable",
        "i am unable",
        "i won't",
        "i will not",
        "i can't assist",
        "i cannot help",
        "i'm sorry, but",
        "i am sorry, but"
    ];

    public override IReadOnlyCollection<string> EvaluationMetricNames => ["Refusal"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("Refusal");
        var text = modelResponse.Text ?? string.Empty;
        var matched = RefusalPhrases.FirstOrDefault(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

        metric.Value = matched is not null;
        metric.Reason = matched is null
            ? "No common refusal phrase detected."
            : $"Detected refusal phrase '{matched}'.";
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

/// <summary>BooleanMetric — response text is parseable JSON.</summary>
public sealed class JsonValidityEvaluator : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["JSON Validity"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(ShapeEvaluatorHelpers.Validate("JSON Validity", modelResponse.Text, text =>
        {
            using var _ = JsonDocument.Parse(text);
        }));
}

/// <summary>BooleanMetric — response text is well-formed XML.</summary>
public sealed class XmlValidityEvaluator : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["XML Validity"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(ShapeEvaluatorHelpers.Validate("XML Validity", modelResponse.Text, text => XDocument.Parse(text)));
}

/// <summary>BooleanMetric — response has plausible HTML shape. This is not W3C validation.</summary>
public sealed class HtmlShapeEvaluator(params string[] requiredTags) : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["HTML Shape"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("HTML Shape");
        var text = modelResponse.Text ?? string.Empty;
        var hasAnyTag = Regex.IsMatch(text, @"<\s*[a-zA-Z][a-zA-Z0-9:-]*(\s|>|/>)");
        var missing = requiredTags
            .Where(tag => !Regex.IsMatch(text, $@"<\s*{Regex.Escape(tag)}(\s|>|/>)", RegexOptions.IgnoreCase))
            .ToList();

        metric.Value = hasAnyTag && missing.Count == 0;
        metric.Reason = metric.Value == true
            ? "Output has plausible HTML shape."
            : !hasAnyTag
                ? "Output does not contain an HTML-like tag."
                : $"Output is missing required tag(s): [{string.Join(", ", missing)}].";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}

/// <summary>BooleanMetric — response has plausible SQL statement shape. This is not dialect validation.</summary>
public sealed class SqlShapeEvaluator : HpdDeterministicEvaluatorBase
{
    private static readonly string[] StatementStarters =
    [
        "select", "insert", "update", "delete", "with", "create", "alter", "drop", "merge"
    ];

    public override IReadOnlyCollection<string> EvaluationMetricNames => ["SQL Shape"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var metric = new BooleanMetric("SQL Shape");
        var text = (modelResponse.Text ?? string.Empty).Trim();
        var startsLikeSql = StatementStarters.Any(s => text.StartsWith(s, StringComparison.OrdinalIgnoreCase));
        var balancedParens = IsBalanced(text, '(', ')');
        var balancedSingleQuotes = text.Count(c => c == '\'') % 2 == 0;
        var balancedDoubleQuotes = text.Count(c => c == '"') % 2 == 0;

        metric.Value = startsLikeSql && balancedParens && balancedSingleQuotes && balancedDoubleQuotes;
        metric.Reason = metric.Value == true
            ? "Output has plausible SQL shape."
            : $"SQL shape failed (startsLikeSql={startsLikeSql}, balancedParens={balancedParens}, balancedSingleQuotes={balancedSingleQuotes}, balancedDoubleQuotes={balancedDoubleQuotes}).";
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }

    private static bool IsBalanced(string text, char open, char close)
    {
        var depth = 0;
        foreach (var ch in text)
        {
            if (ch == open) depth++;
            if (ch == close) depth--;
            if (depth < 0) return false;
        }

        return depth == 0;
    }
}

internal static class ShapeEvaluatorHelpers
{
    internal static EvaluationResult Validate(string metricName, string? text, Action<string> parse)
    {
        var metric = new BooleanMetric(metricName);
        try
        {
            parse(text ?? string.Empty);
            metric.Value = true;
            metric.Reason = $"{metricName} passed.";
        }
        catch (Exception ex) when (ex is JsonException or System.Xml.XmlException or InvalidOperationException)
        {
            metric.Value = false;
            metric.Reason = $"{metricName} failed: {ex.Message}";
        }

        metric.MarkAsHpdBuiltIn();
        return new EvaluationResult(metric);
    }
}
