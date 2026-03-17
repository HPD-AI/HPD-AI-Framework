namespace HPD.ML.BinaryClassification.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using Double = Helium.Primitives.Double;

public class LinearModelParametersTests
{
    [Fact]
    public void Parameters_StoresWeightsAndBias()
    {
        var weights = Vector<Double>.FromArray(new Double(1), new Double(2), new Double(3));
        var bias = new Double(0.5);
        var p = new LinearModelParameters(weights, bias);

        Assert.Equal(1.0, (double)p.Weights[0], 0.001);
        Assert.Equal(2.0, (double)p.Weights[1], 0.001);
        Assert.Equal(3.0, (double)p.Weights[2], 0.001);
        Assert.Equal(0.5, (double)p.Bias, 0.001);
    }

    [Fact]
    public void Parameters_FeatureCount_MatchesWeightLength()
    {
        var weights = Vector<Double>.FromArray(new Double(1), new Double(2));
        var p = new LinearModelParameters(weights, new Double(0));
        Assert.Equal(2, p.FeatureCount);
    }

    [Fact]
    public void Parameters_FeatureNames_Optional()
    {
        var weights = Vector<Double>.FromArray(new Double(1), new Double(2));
        var p = new LinearModelParameters(weights, new Double(0))
        {
            FeatureNames = ["A", "B"]
        };
        Assert.Equal(2, p.FeatureNames!.Count);
        Assert.Equal("A", p.FeatureNames[0]);
    }

    [Fact]
    public void Parameters_Statistics_Optional()
    {
        var weights = Vector<Double>.FromArray(new Double(1));
        var stats = new WeightStatistics(1.0, 0.1, 10.0, 0.001);
        var p = new LinearModelParameters(weights, new Double(0))
        {
            Statistics = [stats]
        };
        Assert.Single(p.Statistics!);
        Assert.Equal(10.0, p.Statistics[0].ZScore);
    }

    [Fact]
    public void Parameters_ImplementsILearnedParameters()
    {
        var weights = Vector<Double>.FromArray(new Double(1));
        var p = new LinearModelParameters(weights, new Double(0));
        Assert.IsAssignableFrom<ILearnedParameters>(p);
    }
}
