namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;

public class ExtensionTests
{
    [Fact]
    public void SsaSpikeDetection_ReturnsLearner()
    {
        ILearner learner = ILearner.SsaSpikeDetection();
        Assert.IsType<SsaAnomalyLearner>(learner);
    }

    [Fact]
    public void SsaChangePointDetection_ReturnsLearner()
    {
        ILearner learner = ILearner.SsaChangePointDetection();
        Assert.IsType<SsaAnomalyLearner>(learner);
    }

    [Fact]
    public void SsaForecasting_ReturnsLearner()
    {
        ILearner learner = ILearner.SsaForecasting();
        Assert.IsType<SsaForecastingLearner>(learner);
    }
}
