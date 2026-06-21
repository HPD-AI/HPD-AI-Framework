namespace HPD.ML.BinaryClassification.Tests;

using HPD.ML.Abstractions;

public class LinearModelParametersTests
{
    [Fact]
    public void Parameters_StoresWeightsAndBias()
    {
        var p = new LinearModelParameters([1, 2, 3], 0.5);

        Assert.Equal(1.0, p.Weights[0], 0.001);
        Assert.Equal(2.0, p.Weights[1], 0.001);
        Assert.Equal(3.0, p.Weights[2], 0.001);
        Assert.Equal(0.5, p.Bias, 0.001);
    }

    [Fact]
    public void Parameters_FeatureCount_MatchesWeightLength()
    {
        var p = new LinearModelParameters([1, 2], 0);
        Assert.Equal(2, p.FeatureCount);
    }

    [Fact]
    public void Parameters_FeatureNames_Optional()
    {
        var p = new LinearModelParameters([1, 2], 0)
        {
            FeatureNames = ["A", "B"]
        };
        Assert.Equal(2, p.FeatureNames!.Count);
        Assert.Equal("A", p.FeatureNames[0]);
    }

    [Fact]
    public void Parameters_Statistics_Optional()
    {
        var stats = new WeightStatistics(1.0, 0.1, 10.0, 0.001);
        var p = new LinearModelParameters([1], 0)
        {
            Statistics = [stats]
        };
        Assert.Single(p.Statistics!);
        Assert.Equal(10.0, p.Statistics[0].ZScore);
    }

    [Fact]
    public void Parameters_ImplementsILearnedParameters()
    {
        var p = new LinearModelParameters([1], 0);
        Assert.IsAssignableFrom<ILearnedParameters>(p);
    }
}
