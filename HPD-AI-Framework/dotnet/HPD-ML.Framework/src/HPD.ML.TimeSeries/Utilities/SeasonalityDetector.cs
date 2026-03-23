using System.Numerics;

namespace HPD.ML.TimeSeries;

/// <summary>
/// FFT-based seasonality period detection.
/// Returns the dominant period or -1 if no significant periodicity found.
/// </summary>
public static class SeasonalityDetector
{
    public static int DetectPeriod(ReadOnlySpan<double> series, int maxLag = 400, double randomnessThreshold = 0.5)
    {
        if (series.Length < 16)
            return -1;

        int n = series.Length;
        int effectiveMaxLag = Math.Min(maxLag, n / 4);
        if (effectiveMaxLag < 2) return -1;

        // Compute autocorrelation via FFT
        int fftSize = FftHelper.NextPowerOfTwo(2 * n);

        var complex = new Complex[fftSize];
        double mean = 0;
        for (int i = 0; i < n; i++) mean += series[i];
        mean /= n;

        for (int i = 0; i < n; i++)
            complex[i] = new Complex(series[i] - mean, 0);

        // FFT → |F|² → IFFT = autocorrelation
        FftHelper.Transform(complex, forward: true);
        for (int i = 0; i < fftSize; i++)
            complex[i] = new Complex(complex[i].Magnitude * complex[i].Magnitude, 0);
        FftHelper.Transform(complex, forward: false);

        double variance = complex[0].Real;
        if (variance < 1e-10)
            return -1;

        var acf = new double[effectiveMaxLag + 1];
        for (int i = 0; i <= effectiveMaxLag; i++)
            acf[i] = complex[i].Real / variance;

        // Find peaks in autocorrelation
        int bestPeriod = -1;
        double bestScore = randomnessThreshold;

        for (int lag = 2; lag <= effectiveMaxLag; lag++)
        {
            bool isPeak = acf[lag] > acf[lag - 1] &&
                          (lag + 1 > effectiveMaxLag || acf[lag] > acf[lag + 1]);
            if (isPeak && acf[lag] > bestScore)
            {
                bestScore = acf[lag];
                bestPeriod = lag;
            }
        }

        return bestPeriod;
    }
}
