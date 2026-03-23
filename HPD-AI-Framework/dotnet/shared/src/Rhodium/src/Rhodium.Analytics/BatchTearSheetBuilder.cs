using Rhodium.Primitives;

namespace Rhodium.Analytics;

/// <summary>
/// Builds BatchTearSheet from individual tear sheets or batch order data.
/// Provides VI-aligned struct-of-arrays output for efficient SIMD operations.
/// </summary>
public static class BatchTearSheetBuilder
{
    /// <summary>
    /// Aggregate individual tear sheets into batch format.
    /// Output is VI-aligned for efficient tensor operations.
    /// </summary>
    /// <param name="tearSheets">Individual tear sheets per variant (must be in VI order)</param>
    /// <returns>Batch tear sheet with struct-of-arrays layout</returns>
    public static BatchTearSheet FromTearSheets(IReadOnlyList<TearSheet> tearSheets)
    {
        if (tearSheets.Count == 0)
            return Empty();

        var totalReturn = new double[tearSheets.Count];
        var cagr = new double[tearSheets.Count];
        var sharpe = new double[tearSheets.Count];
        var maxDrawdown = new double[tearSheets.Count];

        for (int i = 0; i < tearSheets.Count; i++)
        {
            var ts = tearSheets[i];
            totalReturn[i] = (double)ts.TotalReturn;
            cagr[i] = (double)ts.Cagr;
            sharpe[i] = (double)ts.SharpeRatio;
            maxDrawdown[i] = (double)ts.MaxDrawdown;
        }

        return new BatchTearSheet(
            TotalReturn: totalReturn,
            Cagr: cagr,
            Sharpe: sharpe,
            MaxDrawdown: maxDrawdown
        );
    }

    /// <summary>
    /// Build batch tear sheet from variant-indexed round trips.
    /// Groups trades by variant ID and computes metrics per variant.
    /// </summary>
    /// <param name="roundTripsByVariant">Round trips grouped by variant ID</param>
    /// <param name="initialCapital">Initial capital for each variant</param>
    /// <param name="variantCount">Total number of variants (determines output size)</param>
    /// <returns>Batch tear sheet with metrics for all variants</returns>
    public static BatchTearSheet FromRoundTrips(
        IReadOnlyDictionary<int, List<RoundTrip>> roundTripsByVariant,
        Money initialCapital,
        int variantCount)
    {
        var totalReturn = new double[variantCount];
        var cagr = new double[variantCount];
        var sharpe = new double[variantCount];
        var maxDrawdown = new double[variantCount];

        for (int variantId = 0; variantId < variantCount; variantId++)
        {
            if (roundTripsByVariant.TryGetValue(variantId, out var trades) && trades.Count > 0)
            {
                var ts = TearSheet.Calculate(trades, initialCapital);
                totalReturn[variantId] = (double)ts.TotalReturn;
                cagr[variantId] = (double)ts.Cagr;
                sharpe[variantId] = (double)ts.SharpeRatio;
                maxDrawdown[variantId] = (double)ts.MaxDrawdown;
            }
            else
            {
                // No trades for this variant - all zeros
                totalReturn[variantId] = 0.0;
                cagr[variantId] = 0.0;
                sharpe[variantId] = 0.0;
                maxDrawdown[variantId] = 0.0;
            }
        }

        return new BatchTearSheet(
            TotalReturn: totalReturn,
            Cagr: cagr,
            Sharpe: sharpe,
            MaxDrawdown: maxDrawdown
        );
    }

    /// <summary>
    /// Get top N variants by a specific metric.
    /// Useful for filtering best performers in grid search.
    /// </summary>
    /// <param name="batch">Batch tear sheet</param>
    /// <param name="metric">Which metric to sort by</param>
    /// <param name="topN">Number of top variants to return</param>
    /// <returns>Indices of top N variants</returns>
    public static int[] GetTopVariants(
        BatchTearSheet batch,
        BatchMetric metric,
        int topN)
    {
        var values = metric switch
        {
            BatchMetric.TotalReturn => batch.TotalReturn.Span,
            BatchMetric.Cagr => batch.Cagr.Span,
            BatchMetric.Sharpe => batch.Sharpe.Span,
            BatchMetric.MaxDrawdown => batch.MaxDrawdown.Span,
            _ => throw new ArgumentException($"Unknown metric: {metric}")
        };

        // Create (index, value) pairs
        var indexed = new (int Index, double Value)[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            indexed[i] = (i, values[i]);
        }

        // For MaxDrawdown, lower is better (descending sort with negative)
        // For others, higher is better (descending sort)
        var sorted = metric == BatchMetric.MaxDrawdown
            ? indexed.OrderBy(x => x.Value).ToArray()
            : indexed.OrderByDescending(x => x.Value).ToArray();

        return sorted.Take(Math.Min(topN, sorted.Length))
                    .Select(x => x.Index)
                    .ToArray();
    }

    /// <summary>
    /// Calculate summary statistics across all variants.
    /// </summary>
    public static BatchSummary GetSummary(BatchTearSheet batch)
    {
        return new BatchSummary(
            MeanReturn: batch.TotalReturn.Span.ToArray().Average(),
            MedianReturn: Median(batch.TotalReturn.Span),
            StdDevReturn: StdDev(batch.TotalReturn.Span),
            MeanSharpe: batch.Sharpe.Span.ToArray().Average(),
            MedianSharpe: Median(batch.Sharpe.Span),
            BestReturn: batch.TotalReturn.Span.ToArray().Max(),
            WorstReturn: batch.TotalReturn.Span.ToArray().Min(),
            VariantsWithPositiveReturn: batch.TotalReturn.Span.ToArray().Count(x => x > 0)
        );
    }

    private static BatchTearSheet Empty() => new(
        TotalReturn: Array.Empty<double>(),
        Cagr: Array.Empty<double>(),
        Sharpe: Array.Empty<double>(),
        MaxDrawdown: Array.Empty<double>()
    );

    private static double Median(ReadOnlySpan<double> values)
    {
        if (values.Length == 0) return 0.0;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    private static double StdDev(ReadOnlySpan<double> values)
    {
        if (values.Length < 2) return 0.0;
        var mean = values.ToArray().Average();
        var sumSq = 0.0;
        foreach (var v in values)
        {
            var diff = v - mean;
            sumSq += diff * diff;
        }
        return Math.Sqrt(sumSq / (values.Length - 1));
    }
}

/// <summary>
/// Metric to use for ranking variants.
/// </summary>
public enum BatchMetric
{
    TotalReturn,
    Cagr,
    Sharpe,
    MaxDrawdown  // Lower is better
}

/// <summary>
/// Summary statistics across all variants in a batch.
/// </summary>
public readonly record struct BatchSummary(
    double MeanReturn,
    double MedianReturn,
    double StdDevReturn,
    double MeanSharpe,
    double MedianSharpe,
    double BestReturn,
    double WorstReturn,
    int VariantsWithPositiveReturn
);
