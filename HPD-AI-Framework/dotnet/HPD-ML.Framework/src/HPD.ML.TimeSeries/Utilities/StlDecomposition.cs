namespace HPD.ML.TimeSeries;

/// <summary>
/// STL (Seasonal-Trend decomposition using LOESS).
/// Decomposes a time series into seasonal, trend, and residual components.
/// </summary>
public static class StlDecomposition
{
    public static (double[] Seasonal, double[] Trend, double[] Residual) Decompose(
        ReadOnlySpan<double> series, int period, int iterations = 2)
    {
        int n = series.Length;
        if (n < 2 * period)
            throw new ArgumentException($"Series length ({n}) must be at least 2 × period ({2 * period}).");

        var seasonal = new double[n];
        var trend = new double[n];
        var residual = new double[n];

        for (int iter = 0; iter < iterations; iter++)
        {
            // Step 1: Detrend
            var detrended = new double[n];
            for (int i = 0; i < n; i++)
                detrended[i] = series[i] - trend[i];

            // Step 2: Cycle-subseries smoothing
            var smoothedSeasonal = new double[n];
            for (int p = 0; p < period; p++)
            {
                var indices = new List<int>();
                var values = new List<double>();
                for (int i = p; i < n; i += period)
                {
                    indices.Add(i);
                    values.Add(detrended[i]);
                }

                var smoothed = MovingAverageSmooth(values.ToArray(), Math.Max(3, values.Count / 3));
                for (int j = 0; j < indices.Count; j++)
                    smoothedSeasonal[indices[j]] = smoothed[j];
            }

            // Step 3: Low-pass filter
            var lowPass = TripleMovingAverage(smoothedSeasonal, period);

            // Step 4: Seasonal = smoothed - low-pass
            for (int i = 0; i < n; i++)
                seasonal[i] = smoothedSeasonal[i] - lowPass[i];

            // Step 5: Deseasonalize
            var deseasonalized = new double[n];
            for (int i = 0; i < n; i++)
                deseasonalized[i] = series[i] - seasonal[i];

            // Step 6: Trend = smoothed deseasonalized
            trend = MovingAverageSmooth(deseasonalized, Math.Max(3, period));
        }

        // Residual
        for (int i = 0; i < n; i++)
            residual[i] = series[i] - seasonal[i] - trend[i];

        return (seasonal, trend, residual);
    }

    private static double[] MovingAverageSmooth(double[] data, int window)
    {
        int n = data.Length;
        var result = new double[n];
        int half = window / 2;
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            int count = 0;
            for (int j = Math.Max(0, i - half); j <= Math.Min(n - 1, i + half); j++)
            {
                sum += data[j];
                count++;
            }
            result[i] = sum / count;
        }
        return result;
    }

    private static double[] TripleMovingAverage(double[] data, int period)
    {
        var ma1 = MovingAverageSmooth(data, period);
        var ma2 = MovingAverageSmooth(ma1, period);
        return MovingAverageSmooth(ma2, 3);
    }
}
