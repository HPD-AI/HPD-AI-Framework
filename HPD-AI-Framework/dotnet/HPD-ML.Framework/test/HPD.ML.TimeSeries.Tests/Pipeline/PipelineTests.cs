namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;

public class PipelineTests
{
    [Fact]
    public void SsaSpike_TrainAndPredict()
    {
        var trainData = TestHelper.SineData(50, period: 12);
        var testData = TestHelper.SineData(20, period: 12, seed: 99);

        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions { WindowSize = 4 });
        var model = learner.Fit(new LearnerInput(trainData));
        var predictions = model.Transform.Apply(testData);

        Assert.Equal(20, TestHelper.CountRows(predictions));
        var scores = TestHelper.CollectFloat(predictions, "RawScore");
        Assert.All(scores, s => Assert.True(float.IsFinite(s)));
        var alerts = TestHelper.CollectBool(predictions, "Alert");
        Assert.NotNull(alerts);
    }

    [Fact]
    public void SsaForecast_TrainAndPredict()
    {
        var trainData = TestHelper.SineData(50, period: 12);
        var testData = TestHelper.SineData(20, period: 12, seed: 99);

        var learner = new SsaForecastingLearner(options: new SsaForecastOptions
        {
            WindowSize = 4, Horizon = 3
        });
        var model = learner.Fit(new LearnerInput(trainData));
        var predictions = model.Transform.Apply(testData);

        Assert.Equal(20, TestHelper.CountRows(predictions));
        var forecasts = TestHelper.CollectFloatArray(predictions, "Forecast");
        Assert.All(forecasts, f =>
        {
            Assert.Equal(3, f.Length);
            Assert.All(f, v => Assert.True(float.IsFinite(v)));
        });
    }

    [Fact]
    public void IidSpike_DirectApply()
    {
        var data = TestHelper.SineData(30, period: 6);
        var detector = new IidSpikeDetector();
        var output = detector.Apply(data);

        Assert.Equal(30, TestHelper.CountRows(output));
        var pValues = TestHelper.CollectFloat(output, "PValue");
        Assert.All(pValues, p => Assert.True(float.IsFinite(p)));
    }

    [Fact]
    public void SpectralResidual_DirectApply()
    {
        var data = TestHelper.SineData(128, period: 16);
        var detector = new SpectralResidualDetector(windowSize: 64);
        var output = detector.Apply(data);

        Assert.Equal(128, TestHelper.CountRows(output));
        var scores = TestHelper.CollectFloat(output, "RawScore");
        Assert.All(scores, s => Assert.True(float.IsFinite(s)));
        var magnitudes = TestHelper.CollectFloat(output, "Magnitude");
        Assert.All(magnitudes, m => Assert.True(float.IsFinite(m)));
    }
}
