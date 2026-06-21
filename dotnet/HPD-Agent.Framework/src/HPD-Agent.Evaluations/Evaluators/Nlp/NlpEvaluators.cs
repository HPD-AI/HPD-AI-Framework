// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.NLP;
using HPD.Agent.Evaluations.Evaluators;

namespace HPD.Agent.Evaluations.Evaluators.Nlp;

/// <summary>HPD facade over Microsoft's BLEU evaluator.</summary>
public sealed class BleuEvaluator(params string[] references) : HpdDeterministicEvaluatorBase
{
    private readonly BLEUEvaluator _inner = new();

    public override IReadOnlyCollection<string> EvaluationMetricNames => _inner.EvaluationMetricNames;

    protected override async ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var result = await _inner.EvaluateAsync(
            messages,
            modelResponse,
            additionalContext: [new BLEUEvaluatorContext(references)],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        result.MarkAllMetricsAsHpdBuiltIn();
        return result;
    }
}

/// <summary>HPD facade over Microsoft's GLEU evaluator.</summary>
public sealed class GleuEvaluator(params string[] references) : HpdDeterministicEvaluatorBase
{
    private readonly GLEUEvaluator _inner = new();

    public override IReadOnlyCollection<string> EvaluationMetricNames => _inner.EvaluationMetricNames;

    protected override async ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var result = await _inner.EvaluateAsync(
            messages,
            modelResponse,
            additionalContext: [new GLEUEvaluatorContext(references)],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        result.MarkAllMetricsAsHpdBuiltIn();
        return result;
    }
}

/// <summary>HPD facade over Microsoft's word-overlap F1 evaluator.</summary>
public sealed class TextF1Evaluator(string groundTruth) : HpdDeterministicEvaluatorBase
{
    private readonly F1Evaluator _inner = new();

    public override IReadOnlyCollection<string> EvaluationMetricNames => _inner.EvaluationMetricNames;

    protected override async ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var result = await _inner.EvaluateAsync(
            messages,
            modelResponse,
            additionalContext: [new F1EvaluatorContext(groundTruth)],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        result.MarkAllMetricsAsHpdBuiltIn();
        return result;
    }
}

/// <summary>ROUGE metric variant.</summary>
public enum RougeVariant
{
    Rouge1,
    Rouge2,
    RougeL,
    RougeS,
}

/// <summary>NumericMetric 0-1 — ROUGE-N, ROUGE-L, or ROUGE-S recall-oriented overlap.</summary>
public sealed class RougeEvaluator(string reference, RougeVariant variant = RougeVariant.RougeL)
    : HpdDeterministicEvaluatorBase
{
    public override IReadOnlyCollection<string> EvaluationMetricNames => ["ROUGE"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var referenceTokens = NlpText.Tokenize(reference);
        var candidateTokens = NlpText.Tokenize(modelResponse.Text ?? string.Empty);
        var score = variant switch
        {
            RougeVariant.Rouge1 => RougeN(referenceTokens, candidateTokens, 1),
            RougeVariant.Rouge2 => RougeN(referenceTokens, candidateTokens, 2),
            RougeVariant.RougeL => RougeL(referenceTokens, candidateTokens),
            RougeVariant.RougeS => RougeS(referenceTokens, candidateTokens),
            _ => RougeL(referenceTokens, candidateTokens),
        };

        var metric = new NumericMetric("ROUGE")
        {
            Value = Math.Round(score, 4),
            Reason = $"{variant} score: {score:F4}.",
        };
        metric.AddOrUpdateMetadata("rouge-variant", variant.ToString());
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }

    private static double RougeN(IReadOnlyList<string> referenceTokens, IReadOnlyList<string> candidateTokens, int n)
    {
        if (referenceTokens.Count < n)
            return candidateTokens.Count < n ? 1.0 : 0.0;

        var referenceCounts = CountNGrams(referenceTokens, n);
        var candidateCounts = CountNGrams(candidateTokens, n);
        var overlap = referenceCounts.Sum(kvp =>
            Math.Min(kvp.Value, candidateCounts.TryGetValue(kvp.Key, out var count) ? count : 0));
        return (double)overlap / referenceCounts.Values.Sum();
    }

    private static double RougeL(IReadOnlyList<string> referenceTokens, IReadOnlyList<string> candidateTokens)
    {
        if (referenceTokens.Count == 0)
            return candidateTokens.Count == 0 ? 1.0 : 0.0;

        var lcs = LongestCommonSubsequenceLength(referenceTokens, candidateTokens);
        return (double)lcs / referenceTokens.Count;
    }

    private static double RougeS(IReadOnlyList<string> referenceTokens, IReadOnlyList<string> candidateTokens)
    {
        if (referenceTokens.Count < 2)
            return candidateTokens.Count < 2 ? 1.0 : 0.0;

        var referenceCounts = CountSkipBigrams(referenceTokens);
        var candidateCounts = CountSkipBigrams(candidateTokens);
        var overlap = referenceCounts.Sum(kvp =>
            Math.Min(kvp.Value, candidateCounts.TryGetValue(kvp.Key, out var count) ? count : 0));
        return (double)overlap / referenceCounts.Values.Sum();
    }

    private static Dictionary<string, int> CountNGrams(IReadOnlyList<string> tokens, int n)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i <= tokens.Count - n; i++)
        {
            var key = string.Join('\u001f', tokens.Skip(i).Take(n));
            counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
        }

        return counts;
    }

    private static Dictionary<string, int> CountSkipBigrams(IReadOnlyList<string> tokens)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            for (var j = i + 1; j < tokens.Count; j++)
            {
                var key = string.Join('\u001f', tokens[i], tokens[j]);
                counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
            }
        }

        return counts;
    }

    private static int LongestCommonSubsequenceLength(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var previous = new int[b.Count + 1];
        var current = new int[b.Count + 1];

        for (var i = 1; i <= a.Count; i++)
        {
            for (var j = 1; j <= b.Count; j++)
            {
                current[j] = a[i - 1] == b[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }

            (previous, current) = (current, previous);
            Array.Clear(current);
        }

        return previous[b.Count];
    }
}

/// <summary>Options for HPD's dependency-free METEOR-style evaluator.</summary>
public sealed class MeteorEvaluatorOptions
{
    /// <summary>Precision/recall weighting. Default matches common METEOR-style scoring.</summary>
    public double Alpha { get; init; } = 0.9;

    /// <summary>Fragmentation penalty exponent.</summary>
    public double Beta { get; init; } = 3.0;

    /// <summary>Fragmentation penalty weight.</summary>
    public double Gamma { get; init; } = 0.5;
}

/// <summary>NumericMetric 0-1 — lightweight METEOR-style unigram alignment score.</summary>
public sealed class MeteorEvaluator : HpdDeterministicEvaluatorBase
{
    private readonly string[] _references;
    private readonly MeteorEvaluatorOptions _options;

    public MeteorEvaluator(params string[] references)
        : this(new MeteorEvaluatorOptions(), references)
    {
    }

    public MeteorEvaluator(MeteorEvaluatorOptions options, params string[] references)
    {
        _options = options;
        _references = references.Length == 0 ? [string.Empty] : references;
    }

    public override IReadOnlyCollection<string> EvaluationMetricNames => ["METEOR"];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        var candidateTokens = NlpText.Tokenize(modelResponse.Text ?? string.Empty).Select(NlpText.Stem).ToArray();
        var best = ScoreBestReference(_references, candidateTokens);

        var metric = new NumericMetric("METEOR")
        {
            Value = Math.Round(best.Score, 4),
            Reason = $"METEOR-style score: {best.Score:F4}; precision={best.Precision:F4}; recall={best.Recall:F4}; chunks={best.Chunks}.",
        };
        metric.AddOrUpdateMetadata("meteor-matches", best.Matches.ToString(CultureInfo.InvariantCulture));
        metric.AddOrUpdateMetadata("meteor-precision", best.Precision.ToString(CultureInfo.InvariantCulture));
        metric.AddOrUpdateMetadata("meteor-recall", best.Recall.ToString(CultureInfo.InvariantCulture));
        metric.AddOrUpdateMetadata("meteor-chunks", best.Chunks.ToString(CultureInfo.InvariantCulture));
        metric.AddOrUpdateMetadata("meteor-reference-index", best.ReferenceIndex.ToString(CultureInfo.InvariantCulture));
        metric.AddOrUpdateMetadata("meteor-alpha", _options.Alpha.ToString(CultureInfo.InvariantCulture));
        metric.AddOrUpdateMetadata("meteor-beta", _options.Beta.ToString(CultureInfo.InvariantCulture));
        metric.AddOrUpdateMetadata("meteor-gamma", _options.Gamma.ToString(CultureInfo.InvariantCulture));
        metric.MarkAsHpdBuiltIn();
        return ValueTask.FromResult(new EvaluationResult(metric));
    }

    private MeteorScore ScoreBestReference(IReadOnlyList<string> references, IReadOnlyList<string> candidateTokens)
    {
        var best = MeteorScore.Zero;
        for (var i = 0; i < references.Count; i++)
        {
            var referenceTokens = NlpText.Tokenize(references[i]).Select(NlpText.Stem).ToArray();
            var current = Score(referenceTokens, candidateTokens) with { ReferenceIndex = i };
            if (current.Score > best.Score || i == 0)
                best = current;
        }

        return best;
    }

    private MeteorScore Score(
        IReadOnlyList<string> referenceTokens,
        IReadOnlyList<string> candidateTokens)
    {
        if (referenceTokens.Count == 0 || candidateTokens.Count == 0)
        {
            var emptyScore = referenceTokens.Count == 0 && candidateTokens.Count == 0 ? 1.0 : 0.0;
            return new MeteorScore(emptyScore, emptyScore > 0 ? 1 : 0, emptyScore, emptyScore, emptyScore > 0 ? 1 : 0, 0);
        }

        var matches = MatchTokens(referenceTokens, candidateTokens);

        if (matches.Count == 0)
            return MeteorScore.Zero;

        var precision = (double)matches.Count / candidateTokens.Count;
        var recall = (double)matches.Count / referenceTokens.Count;
        var denominator = _options.Alpha * precision + (1 - _options.Alpha) * recall;
        var fMean = denominator == 0.0 ? 0.0 : precision * recall / denominator;
        var chunks = CountChunks(matches);
        var fragmentation = (double)chunks / matches.Count;
        var penalty = _options.Gamma * Math.Pow(fragmentation, _options.Beta);
        var score = Math.Max(0.0, (1.0 - penalty) * fMean);

        return new MeteorScore(score, matches.Count, precision, recall, chunks, 0);
    }

    private static List<MeteorMatch> MatchTokens(
        IReadOnlyList<string> referenceTokens,
        IReadOnlyList<string> candidateTokens)
    {
        var matches = new List<MeteorMatch>();
        var usedReferences = new bool[referenceTokens.Count];

        for (var candidateIndex = 0; candidateIndex < candidateTokens.Count; candidateIndex++)
        {
            for (var referenceIndex = 0; referenceIndex < referenceTokens.Count; referenceIndex++)
            {
                if (usedReferences[referenceIndex] ||
                    !StringComparer.Ordinal.Equals(candidateTokens[candidateIndex], referenceTokens[referenceIndex]))
                {
                    continue;
                }

                usedReferences[referenceIndex] = true;
                matches.Add(new MeteorMatch(candidateIndex, referenceIndex));
                break;
            }
        }

        return matches;
    }

    private static int CountChunks(IReadOnlyList<MeteorMatch> matches)
    {
        if (matches.Count == 0)
            return 0;

        var chunks = 1;
        for (var i = 0; i < matches.Count - 1; i++)
        {
            if (matches[i + 1].CandidateIndex != matches[i].CandidateIndex + 1 ||
                matches[i + 1].ReferenceIndex != matches[i].ReferenceIndex + 1)
            {
                chunks++;
            }
        }

        return chunks;
    }

    private readonly record struct MeteorMatch(int CandidateIndex, int ReferenceIndex);

    private readonly record struct MeteorScore(
        double Score,
        int Matches,
        double Precision,
        double Recall,
        int Chunks,
        int ReferenceIndex)
    {
        public static MeteorScore Zero { get; } = new(0.0, 0, 0.0, 0.0, 0, 0);
    }
}

internal static class NlpText
{
    internal static string[] Tokenize(string text)
    {
        var tokens = new List<string>();
        var start = -1;

        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                    start = i;
                continue;
            }

            if (start >= 0)
            {
                tokens.Add(text[start..i].ToLowerInvariant());
                start = -1;
            }
        }

        if (start >= 0)
            tokens.Add(text[start..].ToLowerInvariant());

        return tokens.ToArray();
    }

    internal static string Stem(string token)
    {
        if (token.Length > 5 && token.EndsWith("ing", StringComparison.Ordinal))
            return RemoveDoubledFinalConsonant(token[..^3]);
        if (token.Length > 4 && token.EndsWith("ed", StringComparison.Ordinal))
            return RemoveDoubledFinalConsonant(token[..^2]);
        if (token.Length > 4 && token.EndsWith("es", StringComparison.Ordinal))
            return token[..^2];
        if (token.Length > 3 && token.EndsWith("s", StringComparison.Ordinal))
            return token[..^1];
        return token;
    }

    private static string RemoveDoubledFinalConsonant(string token)
    {
        if (token.Length >= 2 &&
            token[^1] == token[^2] &&
            "aeiou".IndexOf(token[^1], StringComparison.Ordinal) < 0)
        {
            return token[..^1];
        }

        return token;
    }
}
