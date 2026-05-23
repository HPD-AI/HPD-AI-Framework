namespace Rhodium.Tensor;

/// <summary>
/// Minimal technical indicator helpers for tensor operations.
/// These are allocation-free reference implementations for vector tensor inputs.
/// </summary>
public static class TensorInd
{
    /// <summary>
    /// Tensorized RSI over the current close vector.
    /// Assumes input contains the most recent closes (one per virtual index).
    /// For full historical RSI, callers should supply a rolling window per index.
    /// </summary>
    public static void RSI(ReadOnlySpan<double> closes, Span<double> output, int period = 14)
    {
        if (closes.Length != output.Length)
            throw new ArgumentException("Length mismatch");
        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period), "RSI period must be positive.");

        output.Clear();

        if (closes.Length <= period)
            return;

        var gain = 0.0;
        var loss = 0.0;

        for (var i = 1; i <= period; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change >= 0)
                gain += change;
            else
                loss -= change;
        }

        var averageGain = gain / period;
        var averageLoss = loss / period;
        output[period] = CalculateRsi(averageGain, averageLoss);

        for (var i = period + 1; i < closes.Length; i++)
        {
            var change = closes[i] - closes[i - 1];
            var currentGain = change > 0 ? change : 0.0;
            var currentLoss = change < 0 ? -change : 0.0;

            averageGain = ((averageGain * (period - 1)) + currentGain) / period;
            averageLoss = ((averageLoss * (period - 1)) + currentLoss) / period;
            output[i] = CalculateRsi(averageGain, averageLoss);
        }
    }

    private static double CalculateRsi(double averageGain, double averageLoss)
    {
        if (averageLoss == 0.0)
            return averageGain == 0.0 ? 50.0 : 100.0;

        var rs = averageGain / averageLoss;
        return 100.0 - (100.0 / (1.0 + rs));
    }
}
