using System.Numerics;

namespace HPD.ML.TimeSeries.Tests;

public class FftHelperTests
{
    [Fact]
    public void Transform_DcSignal_AllEnergyAtZero()
    {
        var data = new Complex[] { 1, 1, 1, 1 };
        FftHelper.Transform(data, forward: true);
        Assert.True(Math.Abs(data[0].Real - 4) < 1e-10);
        for (int i = 1; i < 4; i++)
            Assert.True(data[i].Magnitude < 1e-10, $"Bin {i} should be ~0 but was {data[i].Magnitude}");
    }

    [Fact]
    public void Transform_RoundTrip_Preserves()
    {
        var original = new double[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var data = original.Select(x => new Complex(x, 0)).ToArray();
        FftHelper.Transform(data, forward: true);
        FftHelper.Transform(data, forward: false);
        for (int i = 0; i < original.Length; i++)
            Assert.True(Math.Abs(data[i].Real - original[i]) < 1e-10,
                $"Index {i}: expected {original[i]}, got {data[i].Real}");
    }

    [Fact]
    public void Transform_KnownSineWave_PeakAtCorrectBin()
    {
        int N = 16;
        int k = 2;
        var data = new Complex[N];
        for (int i = 0; i < N; i++)
            data[i] = new Complex(Math.Sin(2 * Math.PI * k * i / N), 0);
        FftHelper.Transform(data, forward: true);

        // Peak should be at bin k (and N-k for conjugate)
        double peakMag = data[k].Magnitude;
        for (int i = 1; i < N / 2; i++)
        {
            if (i == k) continue;
            Assert.True(peakMag > data[i].Magnitude * 10,
                $"Peak at bin {k} ({peakMag}) should dominate bin {i} ({data[i].Magnitude})");
        }
    }

    [Fact]
    public void NextPowerOfTwo_Various()
    {
        Assert.Equal(1, FftHelper.NextPowerOfTwo(1));
        Assert.Equal(4, FftHelper.NextPowerOfTwo(3));
        Assert.Equal(8, FftHelper.NextPowerOfTwo(7));
        Assert.Equal(16, FftHelper.NextPowerOfTwo(16));
        Assert.Equal(32, FftHelper.NextPowerOfTwo(17));
    }

    [Fact]
    public void PadToPowerOfTwo_PadsWithZeros()
    {
        var input = new double[] { 1, 2, 3, 4, 5 };
        var result = FftHelper.PadToPowerOfTwo(input);
        Assert.Equal(8, result.Length);
        Assert.Equal(5, result[4].Real);
        Assert.Equal(0, result[5].Real);
        Assert.Equal(0, result[6].Real);
        Assert.Equal(0, result[7].Real);
    }
}
