using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Quant;

namespace Rhodium.Events.Tests;

public class QuantEventTests
{
    [Fact]
    public void QuantResultReady_CreatesCorrectly()
    {
        var result = new QuantResult
        {
            Sequence = new Sequence(100),
            BatchMapVersion = 5,
            ComputationName = "Covariance",
            Data = new double[] { 1.0, 2.0, 3.0 }
        };

        var evt = new QuantResultReady(result);

        Assert.Equal(result, evt.Result);
        Assert.Equal(new Sequence(100), evt.Result.Sequence);
        Assert.Equal(5, evt.Result.BatchMapVersion);
        Assert.Equal("Covariance", evt.Result.ComputationName);
    }

    [Fact]
    public void QuantResultReady_HasCorrectChannel()
    {
        var result = new QuantResult
        {
            Sequence = new Sequence(1),
            BatchMapVersion = 1,
            ComputationName = "Test"
        };

        var evt = new QuantResultReady(result);

        Assert.Equal(HPD.Events.EventChannel.Synchronous, evt.Channel);
    }

    [Fact]
    public void QuantResultReady_HasCorrectEventKind()
    {
        var result = new QuantResult
        {
            Sequence = new Sequence(1),
            BatchMapVersion = 1,
            ComputationName = "Test"
        };

        var evt = new QuantResultReady(result);

        Assert.Equal(HPD.Events.EventKind.Content, evt.Kind);
    }

    [Fact]
    public void QuantResult_SupportsVersionGating()
    {
        var result1 = new QuantResult
        {
            Sequence = new Sequence(100),
            BatchMapVersion = 5,
            ComputationName = "Test"
        };

        var result2 = new QuantResult
        {
            Sequence = new Sequence(101),
            BatchMapVersion = 5,
            ComputationName = "Test"
        };

        // Results with different sequences should be different
        Assert.NotEqual(result1.Sequence, result2.Sequence);
        Assert.Equal(result1.BatchMapVersion, result2.BatchMapVersion);
    }

    [Fact]
    public void QuantResult_SupportsArbitraryData()
    {
        // Test with different data types
        var result1 = new QuantResult
        {
            Sequence = new Sequence(1),
            BatchMapVersion = 1,
            ComputationName = "DoubleArray",
            Data = new double[] { 1.0, 2.0, 3.0 }
        };

        var result2 = new QuantResult
        {
            Sequence = new Sequence(2),
            BatchMapVersion = 1,
            ComputationName = "Dictionary",
            Data = new Dictionary<string, double> { ["mean"] = 0.5, ["std"] = 0.2 }
        };

        Assert.IsType<double[]>(result1.Data);
        Assert.IsType<Dictionary<string, double>>(result2.Data);
    }
}
