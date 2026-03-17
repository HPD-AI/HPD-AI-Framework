namespace HPD.ML.BinaryClassification.Tests;

using HPD.ML.Abstractions;

public class ExtensionTests
{
    [Fact]
    public void Ext_ILearner_LogisticRegression()
    {
        var learner = ILearner.LogisticRegression();
        Assert.IsType<LogisticRegressionLearner>(learner);
    }

    [Fact]
    public void Ext_ILearner_Sdca()
    {
        var learner = ILearner.Sdca();
        Assert.IsType<SdcaLearner>(learner);
    }

    [Fact]
    public void Ext_ILearner_AveragedPerceptron()
    {
        var learner = ILearner.AveragedPerceptron();
        Assert.IsType<AveragedPerceptronLearner>(learner);
    }

    [Fact]
    public void Ext_ILearner_LinearSvm()
    {
        var learner = ILearner.LinearSvm();
        Assert.IsType<LinearSvmLearner>(learner);
    }
}
