using Rhodium.Primitives;
using Rhodium.Quant;

namespace Rhodium.Quant.Tests;

/// <summary>
/// Tests for QuantResult payload.
/// </summary>
public class QuantResultTests
{
    [Fact]
    public void Result_InitializesWithSequenceAndVersion()
    {
        var result = new QuantResult
        {
            Sequence = new Sequence(100),
            BatchMapVersion = 5,
            ComputationName = "TestComputation",
            Data = null
        };

        Assert.Equal(new Sequence(100), result.Sequence);
        Assert.Equal(5, result.BatchMapVersion);
        Assert.Equal("TestComputation", result.ComputationName);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Result_WithWeightsData()
    {
        var weights = new double[] { 0.3, 0.5, 0.2 };

        var result = new QuantResult
        {
            Sequence = new Sequence(50),
            BatchMapVersion = 3,
            ComputationName = "PortfolioOptimization",
            Data = weights.AsMemory()
        };

        Assert.Equal(new Sequence(50), result.Sequence);
        Assert.Equal(3, result.BatchMapVersion);
        Assert.Equal("PortfolioOptimization", result.ComputationName);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public void Result_WithNullData_IsValid()
    {
        var result = new QuantResult
        {
            Sequence = new Sequence(10),
            BatchMapVersion = 1,
            ComputationName = "EmptyResult",
            Data = null
        };

        Assert.Equal("EmptyResult", result.ComputationName);
        Assert.Null(result.Data);
    }

    [Fact]
    public void Result_WithEmptyComputationName()
    {
        var result = new QuantResult
        {
            Sequence = new Sequence(1),
            BatchMapVersion = 1,
            ComputationName = "",
            Data = null
        };

        Assert.Equal("", result.ComputationName);
    }

    [Fact]
    public void Result_AccessesSequenceAndVersion()
    {
        var result = new QuantResult
        {
            Sequence = new Sequence(999),
            BatchMapVersion = 42,
            ComputationName = "Test",
            Data = null
        };

        Assert.Equal(new Sequence(999), result.Sequence);
        Assert.Equal(42, result.BatchMapVersion);
    }

    [Fact]
    public void Result_WithComplexData()
    {
        var complexData = new
        {
            Weights = new double[] { 0.5, 0.5 },
            Covariance = new double[,] { { 0.1, 0.05 }, { 0.05, 0.2 } },
            ExpectedReturn = 0.08
        };

        var result = new QuantResult
        {
            Sequence = new Sequence(100),
            BatchMapVersion = 5,
            ComputationName = "MeanVariance",
            Data = complexData
        };

        Assert.NotNull(result.Data);
        // Anonymous type is subtype of object
        Assert.IsAssignableFrom<object>(result.Data);
    }

    [Fact]
    public void Result_MultipleResults_IndependentData()
    {
        var result1 = new QuantResult
        {
            Sequence = new Sequence(1),
            BatchMapVersion = 1,
            ComputationName = "Comp1",
            Data = "Data1"
        };

        var result2 = new QuantResult
        {
            Sequence = new Sequence(2),
            BatchMapVersion = 2,
            ComputationName = "Comp2",
            Data = "Data2"
        };

        Assert.NotEqual(result1.Sequence, result2.Sequence);
        Assert.NotEqual(result1.BatchMapVersion, result2.BatchMapVersion);
        Assert.NotEqual(result1.Data, result2.Data);
    }
}
