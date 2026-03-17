namespace HPD.ML.TimeSeries;

/// <summary>
/// Shared anomaly scoring pipeline: raw score → p-value (KDE) → martingale → alert.
/// </summary>
public static class AnomalyScorer
{
    public static double ComputeRawScore(double actual, double predicted, ErrorFunction errorFunc)
    {
        return errorFunc switch
        {
            ErrorFunction.SignedDifference => actual - predicted,
            ErrorFunction.AbsoluteDifference => Math.Abs(actual - predicted),
            ErrorFunction.SignedProportion => predicted == 0 ? 0 : (actual - predicted) / predicted,
            ErrorFunction.AbsoluteProportion => predicted == 0 ? 0 : Math.Abs(actual - predicted) / Math.Abs(predicted),
            ErrorFunction.SquaredDifference => (actual - predicted) * (actual - predicted),
            _ => actual - predicted
        };
    }

    public static double ComputeKdePValue(
        double score,
        SlidingWindow<double> scoreHistory,
        AnomalySide side)
    {
        if (scoreHistory.Count < 2)
            return 0.5;

        int n = scoreHistory.Count;
        double median = GetMedian(scoreHistory);
        double mad = GetMedianAbsoluteDeviation(scoreHistory, median);
        double bandwidth = Math.Max(mad * 1.4826 / Math.Pow(n, 0.2), 1e-10);

        double countAbove = 0;
        for (int i = 0; i < n; i++)
        {
            double z = (score - scoreHistory[i]) / bandwidth;
            countAbove += Math.Exp(-0.5 * z * z);
        }

        double pValue = side switch
        {
            AnomalySide.TwoSided => 2.0 * Math.Min(countAbove / n, 1.0 - countAbove / n),
            AnomalySide.PositiveOnly => 1.0 - countAbove / n,
            AnomalySide.NegativeOnly => countAbove / n,
            _ => countAbove / n
        };

        return Math.Clamp(pValue, 0, 1);
    }

    public static double UpdateMartingale(
        double currentLogMartingale,
        double pValue,
        MartingaleType type,
        double powerParam = 0.05)
    {
        double logBet = type switch
        {
            MartingaleType.Power => (powerParam - 1) * Math.Log(Math.Max(pValue, 1e-300)),
            MartingaleType.Mixture => Math.Log(IntegratedPowerBet(pValue, 0.0, 1.0)),
            _ => 0
        };

        return currentLogMartingale + logBet;
    }

    public static bool ShouldAlert(
        double rawScore, double pValue, double logMartingale,
        AlertingMode mode, double threshold)
    {
        return mode switch
        {
            AlertingMode.RawScore => Math.Abs(rawScore) >= threshold,
            AlertingMode.PValueScore => pValue <= threshold,
            AlertingMode.MartingaleScore => logMartingale >= Math.Log(threshold),
            _ => false
        };
    }

    private static double IntegratedPowerBet(double p, double lo, double hi)
    {
        int steps = 20;
        double sum = 0;
        double dx = (hi - lo) / steps;
        for (int i = 0; i < steps; i++)
        {
            double eps = lo + (i + 0.5) * dx;
            sum += Math.Pow(Math.Max(p, 1e-300), eps);
        }
        return sum * dx;
    }

    private static double GetMedian(SlidingWindow<double> window)
    {
        var sorted = new double[window.Count];
        window.CopyTo(sorted);
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
    }

    private static double GetMedianAbsoluteDeviation(SlidingWindow<double> window, double median)
    {
        var deviations = new double[window.Count];
        for (int i = 0; i < window.Count; i++)
            deviations[i] = Math.Abs(window[i] - median);
        Array.Sort(deviations);
        int mid = deviations.Length / 2;
        return deviations.Length % 2 == 0 ? (deviations[mid - 1] + deviations[mid]) / 2 : deviations[mid];
    }
}
