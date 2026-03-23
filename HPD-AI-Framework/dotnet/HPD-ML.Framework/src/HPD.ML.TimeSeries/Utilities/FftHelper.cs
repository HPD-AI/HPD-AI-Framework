using System.Numerics;

namespace HPD.ML.TimeSeries;

/// <summary>
/// In-place Cooley-Tukey radix-2 FFT. Shared by SpectralResidualDetector and SeasonalityDetector.
/// Input length must be a power of 2.
/// </summary>
internal static class FftHelper
{
    public static void Transform(Complex[] data, bool forward)
    {
        int n = data.Length;
        if (n <= 1) return;

        // Bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }

        // Butterfly stages
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = (forward ? -1 : 1) * 2 * Math.PI / len;
            var wlen = Complex.FromPolarCoordinates(1, angle);
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int j = 0; j < len / 2; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + len / 2] * w;
                    data[i + j] = u + v;
                    data[i + j + len / 2] = u - v;
                    w *= wlen;
                }
            }
        }

        if (!forward)
        {
            for (int i = 0; i < n; i++)
                data[i] /= n;
        }
    }

    /// <summary>Returns the smallest power of 2 >= n.</summary>
    public static int NextPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    /// <summary>Pad data to power-of-2 length with zeros.</summary>
    public static Complex[] PadToPowerOfTwo(ReadOnlySpan<double> data)
    {
        int fftSize = NextPowerOfTwo(data.Length);
        var result = new Complex[fftSize];
        for (int i = 0; i < data.Length; i++)
            result[i] = new Complex(data[i], 0);
        return result;
    }
}
