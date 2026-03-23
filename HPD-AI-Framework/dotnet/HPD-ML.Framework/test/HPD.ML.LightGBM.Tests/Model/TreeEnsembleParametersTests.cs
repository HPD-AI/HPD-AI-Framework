namespace HPD.ML.LightGBM.Tests;

public class TreeEnsembleParametersTests
{
    [Fact]
    public void Constructor_StoresEnsemble()
    {
        var trees = new[] { TestHelper.SingleLeafTree(1.0), TestHelper.SingleLeafTree(2.0) };
        var ensemble = new TreeEnsemble(trees, 0);
        var parameters = new TreeEnsembleParameters(ensemble);

        Assert.Same(ensemble, parameters.Ensemble);
        Assert.Equal(2, parameters.Ensemble.Trees.Count);
    }

    [Fact]
    public void Constructor_StoresFeatureImportance()
    {
        var ensemble = new TreeEnsemble([], 0);
        var importance = new double[] { 0.5, 0.3, 0.2 };
        var parameters = new TreeEnsembleParameters(ensemble, importance);

        Assert.NotNull(parameters.FeatureImportance);
        Assert.Equal(3, parameters.FeatureImportance!.Length);
        Assert.Equal(0.5, parameters.FeatureImportance[0]);
        Assert.Equal(0.3, parameters.FeatureImportance[1]);
        Assert.Equal(0.2, parameters.FeatureImportance[2]);
    }
}
