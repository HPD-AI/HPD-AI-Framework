using Rhodium.Tensor;

namespace Rhodium.Tensor.Tests;

/// <summary>
/// Tests for TensorInd technical indicators.
/// </summary>
public class TensorIndTests
{
    [Fact]
    public void RSI_ComputesRollingWilderValues()
    {
        var closes = new double[] { 100, 102, 101, 103, 105, 104, 106 };
        var output = new double[closes.Length];

        TensorInd.RSI(closes, output, period: 3);

        Assert.Equal(0.0, output[0]);
        Assert.Equal(0.0, output[1]);
        Assert.Equal(0.0, output[2]);
        Assert.InRange(output[3], 79.99, 80.01);
        Assert.InRange(output[4], 87.49, 87.51);
        Assert.InRange(output[5], 68.29, 68.30);
        Assert.InRange(output[6], 80.88, 80.89);
    }

    [Fact]
    public void RSI_HandlesDifferentPeriods()
    {
        var closes = new double[20];
        Array.Fill(closes, 100.0);
        var output = new double[20];

        // Should handle different period parameters
        TensorInd.RSI(closes, output, period: 7);
        Assert.Equal(50.0, output[7]);

        TensorInd.RSI(closes, output, period: 14);
        Assert.Equal(50.0, output[14]);

        TensorInd.RSI(closes, output, period: 21);
        Assert.All(output, x => Assert.Equal(0.0, x));
    }

    [Fact]
    public void RSI_ThrowsOnLengthMismatch()
    {
        var closes = new double[10];
        var output = new double[5];

        Assert.Throws<ArgumentException>(() => TensorInd.RSI(closes, output));
    }

    [Fact]
    public void RSI_ThrowsOnInvalidPeriod()
    {
        var closes = new double[10];
        var output = new double[10];

        Assert.Throws<ArgumentOutOfRangeException>(() => TensorInd.RSI(closes, output, period: 0));
    }

    [Fact]
    public void RSI_HandlesEmptyArrays()
    {
        var closes = Array.Empty<double>();
        var output = Array.Empty<double>();

        // Should not throw
        TensorInd.RSI(closes, output);
    }

    [Fact]
    public void RSI_DefaultPeriodIs14()
    {
        var closes = new double[20];
        Array.Fill(closes, 100.0);
        var output = new double[20];

        // Should use default period of 14 when not specified
        TensorInd.RSI(closes, output);

        Assert.Equal(50.0, output[14]);
    }

    [Fact]
    public void RSI_HandlesLargeArrays()
    {
        // Test with 1000 instruments
        var closes = new double[1000];
        for (int i = 0; i < closes.Length; i++)
            closes[i] = 100 + (i % 10);

        var output = new double[1000];

        TensorInd.RSI(closes, output);

        Assert.Contains(output, static x => x > 0.0);
    }

    [Fact]
    public void RSI_AcceptsReadOnlySpan()
    {
        // Verify it works with ReadOnlySpan
        Span<double> closes = stackalloc double[] { 100, 101, 102, 103, 104 };
        Span<double> output = stackalloc double[5];

        TensorInd.RSI(closes, output);

        Assert.Equal(0.0, output[0]);
    }
}
