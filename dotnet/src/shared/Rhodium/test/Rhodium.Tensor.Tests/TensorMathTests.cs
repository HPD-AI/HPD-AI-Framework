using Rhodium.Tensor;

namespace Rhodium.Tensor.Tests;

/// <summary>
/// Tests for TensorMath SIMD operations.
/// </summary>
public class TensorMathTests
{
    [Fact]
    public void ZScore_CalculatesCorrectly_ForStandardInput()
    {
        // Input: [1, 2, 3, 4, 5]
        // Mean = 3, StdDev = sqrt(2)
        // Expected Z-Scores: [-1.414, -0.707, 0, 0.707, 1.414]
        var input = new double[] { 1, 2, 3, 4, 5 };
        var output = new double[5];

        TensorMath.ZScore(input, output);

        Assert.InRange(output[0], -1.42, -1.41);  // -1.414...
        Assert.InRange(output[1], -0.71, -0.70);  // -0.707...
        Assert.InRange(output[2], -0.01, 0.01);   // 0
        Assert.InRange(output[3], 0.70, 0.71);    // 0.707...
        Assert.InRange(output[4], 1.41, 1.42);    // 1.414...
    }

    [Fact]
    public void ZScore_HandlesZeroStdDev()
    {
        // All values identical -> StdDev = 0 -> should clear output
        var input = new double[] { 5, 5, 5, 5, 5 };
        var output = new double[5];

        TensorMath.ZScore(input, output);

        Assert.All(output, x => Assert.Equal(0.0, x));
    }

    [Fact]
    public void ZScore_HandlesNegativeValues()
    {
        var input = new double[] { -10, -5, 0, 5, 10 };
        var output = new double[5];

        TensorMath.ZScore(input, output);

        // Mean = 0, StdDev = sqrt(50) ≈ 7.07
        Assert.InRange(output[0], -1.42, -1.41);  // -10/7.07
        Assert.InRange(output[2], -0.01, 0.01);   // 0
        Assert.InRange(output[4], 1.41, 1.42);    // 10/7.07
    }

    [Fact]
    public void ZScore_HandlesLargeArrays()
    {
        // Test SIMD vectorization with larger arrays
        var input = new double[1000];
        for (int i = 0; i < input.Length; i++)
            input[i] = i;

        var output = new double[1000];

        TensorMath.ZScore(input, output);

        // Mean = 499.5
        // First element should be most negative
        Assert.True(output[0] < output[500]);
        // Middle should be near zero
        Assert.InRange(output[500], -0.1, 0.1);
        // Last element should be most positive
        Assert.True(output[999] > output[500]);
    }

    [Fact]
    public void ZScore_ThrowsOnLengthMismatch()
    {
        var input = new double[5];
        var output = new double[3];

        Assert.Throws<ArgumentException>(() => TensorMath.ZScore(input, output));
    }

    [Fact]
    public void ZScore_HandlesEmptyArrays()
    {
        var input = Array.Empty<double>();
        var output = Array.Empty<double>();

        // Should not throw, but result is meaningless
        // Division by zero in mean calculation would give NaN
        TensorMath.ZScore(input, output);
    }

    [Fact]
    public void ZScore_TypedOverload_WorksWithPriceF64()
    {
        // Test the generic overload with a typed wrapper
        var input = new PriceF64[]
        {
            new(100),
            new(200),
            new(300),
            new(400),
            new(500)
        };
        var output = new PriceF64[5];

        TensorMath.ZScore(input, output);

        // Should behave identically to double version
        Assert.InRange(output[0].Value, -1.42, -1.41);
        Assert.InRange(output[2].Value, -0.01, 0.01);
        Assert.InRange(output[4].Value, 1.41, 1.42);
    }

    [Fact]
    public void ZScore_ProducesNormalizedDistribution()
    {
        // Z-scores should have mean ≈ 0 and stddev ≈ 1
        var input = new double[] { 10, 20, 15, 25, 30, 12, 18, 22 };
        var output = new double[input.Length];

        TensorMath.ZScore(input, output);

        // Calculate mean of z-scores (should be ~0)
        var mean = output.Sum() / output.Length;
        Assert.InRange(mean, -0.0001, 0.0001);

        // Calculate stddev of z-scores (should be ~1)
        var variance = output.Sum(x => x * x) / output.Length;
        var stdDev = Math.Sqrt(variance);
        Assert.InRange(stdDev, 0.99, 1.01);
    }
}
