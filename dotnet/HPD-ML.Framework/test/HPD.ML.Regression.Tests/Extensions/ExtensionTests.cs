namespace HPD.ML.Regression.Tests;

using HPD.ML.Abstractions;

public class ExtensionTests
{
    [Fact]
    public void Ext_ILearner_OrdinaryLeastSquares()
    {
        var learner = ILearner.OrdinaryLeastSquares();
        Assert.IsType<OrdinaryLeastSquaresLearner>(learner);
    }

    [Fact]
    public void Ext_ILearner_SdcaRegression()
    {
        var learner = ILearner.SdcaRegression();
        Assert.IsType<SdcaRegressionLearner>(learner);
    }

    [Fact]
    public void Ext_ILearner_OnlineGradientDescent()
    {
        var learner = ILearner.OnlineGradientDescent();
        Assert.IsType<OnlineGradientDescentLearner>(learner);
    }

    [Fact]
    public void Ext_ILearner_PoissonRegression()
    {
        var learner = ILearner.PoissonRegression();
        Assert.IsType<PoissonRegressionLearner>(learner);
    }
}
