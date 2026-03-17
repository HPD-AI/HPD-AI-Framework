using HPD.RAG.Core.Context;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.AI;

namespace HPD.RAG.Evaluation.Handlers;

/// <summary>
/// Computes BLEU-4 (with brevity penalty) between a model response and one or more
/// reference strings.  No LLM call is made — this is a purely algorithmic handler.
/// </summary>
/// <remarks>
/// Algorithm:
/// <list type="bullet">
///   <item>Tokenizes by splitting on whitespace (case-insensitive).</item>
///   <item>Computes modified n-gram precision for n = 1, 2, 3, 4.</item>
///   <item>
///     When multiple references are provided the best-matching reference is used
///     for the brevity-penalty length calculation (closest length to hypothesis
///     that does not underestimate).
///   </item>
///   <item>
///     Brevity penalty: BP = 1 when hyp_len &gt; ref_len,
///     otherwise BP = exp(1 − ref_len / hyp_len).
///   </item>
///   <item>BLEU = BP * exp(∑ w_n * log(p_n))  where w_n = 0.25 for all n.</item>
///   <item>Returns 0.0 for an empty hypothesis instead of NaN or an exception.</item>
/// </list>
/// </remarks>
[GraphNodeHandler(NodeName = "EvalBLEU")]
public sealed partial class BLEUEvalHandler : HPDAgent.Graph.Abstractions.Handlers.IGraphNodeHandler<HPD.RAG.Core.Context.MragPipelineContext>
{
    /// <summary>Default error propagation: isolate so downstream eval nodes still run.</summary>
    public static Core.Pipeline.MragErrorPropagation DefaultPropagation { get; } =
        Core.Pipeline.MragErrorPropagation.Isolate;

    public Task<Output> ExecuteAsync(
        [InputSocket(Description = "The model response to evaluate")]
        ChatResponse Response,
        [InputSocket(Description = "One or more reference strings to compare against")]
        string[] References,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (References == null || References.Length == 0)
            throw new ArgumentException("At least one reference must be provided.", nameof(References));

        string hypothesis = Response?.Text ?? string.Empty;
        string[] hypTokens = Tokenize(hypothesis);

        if (hypTokens.Length == 0)
            return Task.FromResult(new Output { Score = 0.0 });

        // Tokenize all references.
        string[][] refTokensArray = new string[References.Length][];
        for (int i = 0; i < References.Length; i++)
            refTokensArray[i] = Tokenize(References[i] ?? string.Empty);

        // BLEU-4: modified n-gram precision for n = 1..4.
        double logSum = 0.0;
        const int MaxN = 4;
        const double Weight = 1.0 / MaxN;

        for (int n = 1; n <= MaxN; n++)
        {
            double precision = ModifiedNgramPrecision(hypTokens, refTokensArray, n);

            // If any order has zero precision the whole score is 0.
            if (precision <= 0.0)
                return Task.FromResult(new Output { Score = 0.0 });

            logSum += Weight * Math.Log(precision);
        }

        // Brevity penalty — use the reference whose length is closest to the
        // hypothesis length without being shorter (standard BLEU closest-length rule).
        int hypLen = hypTokens.Length;
        int bestRefLen = PickBestReferenceLength(hypLen, refTokensArray);

        double bp = hypLen >= bestRefLen
            ? 1.0
            : Math.Exp(1.0 - (double)bestRefLen / hypLen);

        double bleu = bp * Math.Exp(logSum);

        // Clamp to [0, 1] to guard against floating-point edge-cases.
        bleu = Math.Max(0.0, Math.Min(1.0, bleu));

        return Task.FromResult(new Output { Score = bleu });
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private static string[] Tokenize(string text) =>
        text.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Computes modified n-gram precision: clipped count / hypothesis n-gram count.
    /// Each n-gram in the hypothesis is capped at its maximum occurrence across all references.
    /// </summary>
    private static double ModifiedNgramPrecision(
        string[] hyp,
        string[][] refs,
        int n)
    {
        if (hyp.Length < n)
            return 0.0;

        // Count hypothesis n-grams.
        var hypCounts = CountNgrams(hyp, n);
        int hypTotal = hyp.Length - n + 1;

        // For each unique n-gram, find max count across all references.
        double clippedTotal = 0.0;
        foreach (var kvp in hypCounts)
        {
            int maxRefCount = 0;
            foreach (var refTokens in refs)
            {
                if (refTokens.Length < n) continue;
                var refCounts = CountNgrams(refTokens, n);
                if (refCounts.TryGetValue(kvp.Key, out int refCount))
                    maxRefCount = Math.Max(maxRefCount, refCount);
            }
            clippedTotal += Math.Min(kvp.Value, maxRefCount);
        }

        return hypTotal == 0 ? 0.0 : clippedTotal / hypTotal;
    }

    private static Dictionary<string, int> CountNgrams(string[] tokens, int n)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i <= tokens.Length - n; i++)
        {
            // Build n-gram key.
            var key = string.Join(" ", tokens, i, n);
            counts.TryGetValue(key, out int existing);
            counts[key] = existing + 1;
        }
        return counts;
    }

    /// <summary>
    /// Selects the reference length closest to the hypothesis length.
    /// When two references are equidistant, picks the shorter one (standard BLEU).
    /// </summary>
    private static int PickBestReferenceLength(int hypLen, string[][] refTokensArray)
    {
        int bestLen = refTokensArray[0].Length;
        int bestDiff = Math.Abs(hypLen - bestLen);

        for (int i = 1; i < refTokensArray.Length; i++)
        {
            int len = refTokensArray[i].Length;
            int diff = Math.Abs(hypLen - len);

            if (diff < bestDiff || (diff == bestDiff && len < bestLen))
            {
                bestLen = len;
                bestDiff = diff;
            }
        }

        return bestLen;
    }

    public sealed record Output
    {
        [OutputSocket(Description = "BLEU-4 score in [0.0, 1.0]")]
        public double Score { get; init; }
    }
}
