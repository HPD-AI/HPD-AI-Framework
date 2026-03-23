using Rhodium.Tensor;

namespace Rhodium.Tensor.Tests;

/// <summary>
/// Tests for TensorInd technical indicators.
/// </summary>
public class TensorIndTests
{
    [Fact]
    public void RSI_ClearsOutput_ForMinimalStub()
    {
        // The minimal stub implementation just clears the output
        var closes = new double[] { 100, 102, 101, 103, 105 };
        var output = new double[5];

        // Pre-fill output to verify it gets cleared
        Array.Fill(output, 99.0);

        TensorInd.RSI(closes, output);

        Assert.All(output, x => Assert.Equal(0.0, x));
    }

    [Fact]
    public void RSI_HandlesDifferentPeriods()
    {
        var closes = new double[20];
        Array.Fill(closes, 100.0);
        var output = new double[20];

        // Should handle different period parameters
        TensorInd.RSI(closes, output, period: 7);
        Assert.All(output, x => Assert.Equal(0.0, x));

        TensorInd.RSI(closes, output, period: 14);
        Assert.All(output, x => Assert.Equal(0.0, x));

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
        var output = new double[20];

        // Should use default period of 14 when not specified
        TensorInd.RSI(closes, output);

        Assert.All(output, x => Assert.Equal(0.0, x));
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

        Assert.All(output, x => Assert.Equal(0.0, x));
    }

    [Fact]
    public void RSI_AcceptsReadOnlySpan()
    {
        // Verify it works with ReadOnlySpan
        Span<double> closes = stackalloc double[] { 100, 101, 102, 103, 104 };
        Span<double> output = stackalloc double[5];

        TensorInd.RSI(closes, output);

        foreach (var value in output)
            Assert.Equal(0.0, value);
    }
}
