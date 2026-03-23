namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;

public class SsaForecastingLearnerTests
{
    [Fact]
    public void Fit_ReturnsModel()
    {
        var data = TestHelper.SineData(50, period: 12);
        var learner = new SsaForecastingLearner(options: new SsaForecastOptions
        {
            WindowSize = 8, Horizon = 3
        });
        var model = learner.Fit(new LearnerInput(data));
        Assert.NotNull(model);
        Assert.IsType<SsaModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_TransformOutputsForecasts()
    {
        var data = TestHelper.SineData(50, period: 12);
        var learner = new SsaForecastingLearner(options: new SsaForecastOptions
        {
            WindowSize = 4, Horizon = 3
        });
        var model = learner.Fit(new LearnerInput(data));
        var output = model.Transform.Apply(TestHelper.SineData(20, period: 12));
        Assert.NotNull(output.Schema.FindByName("Forecast"));
        var forecasts = TestHelper.CollectFloatArray(output, "Forecast");
        Assert.All(forecasts, f => Assert.Equal(3, f.Length));
    }

    [Fact]
    public void Fit_ConfidenceIntervals_LowerLessThanUpper()
    {
        var data = TestHelper.SineData(50, period: 12);
        var learner = new SsaForecastingLearner(options: new SsaForecastOptions
        {
            WindowSize = 4, Horizon = 3, ConfidenceLevel = 0.95f
        });
        var model = learner.Fit(new LearnerInput(data));
        var output = model.Transform.Apply(TestHelper.SineData(20, period: 12));
        var forecasts = TestHelper.CollectFloatArray(output, "Forecast");
        var lowers = TestHelper.CollectFloatArray(output, "LowerBound");
        var uppers = TestHelper.CollectFloatArray(output, "UpperBound");

        // Check post-buffering rows only
        for (int r = 4; r < forecasts.Count; r++)
        {
            for (int h = 0; h < 3; h++)
            {
                Assert.True(lowers[r][h] <= forecasts[r][h] + 1e-5f,
                    $"Row {r}, h={h}: lower {lowers[r][h]} > forecast {forecasts[r][h]}");
                Assert.True(forecasts[r][h] <= uppers[r][h] + 1e-5f,
                    $"Row {r}, h={h}: forecast {forecasts[r][h]} > upper {uppers[r][h]}");
            }
        }
    }

    [Fact]
    public void Fit_ConfidenceIntervals_WidenWithHorizon()
    {
        var data = TestHelper.SineData(50, period: 12);
        var learner = new SsaForecastingLearner(options: new SsaForecastOptions
        {
            WindowSize = 4, Horizon = 5, ConfidenceLevel = 0.95f
        });
        var model = learner.Fit(new LearnerInput(data));
        var output = model.Transform.Apply(TestHelper.SineData(20, period: 12));
        var lowers = TestHelper.CollectFloatArray(output, "LowerBound");
        var uppers = TestHelper.CollectFloatArray(output, "UpperBound");

        // Check a post-buffering row
        int row = 10;
        float width0 = uppers[row][0] - lowers[row][0];
        float width4 = uppers[row][4] - lowers[row][4];
        Assert.True(width4 >= width0 - 1e-5f,
            $"Width at h=4 ({width4}) should be >= width at h=0 ({width0})");
    }

    [Fact]
    public void Fit_BufferingPhase_EmptyForecasts()
    {
        var data = TestHelper.SineData(50, period: 12);
        var learner = new SsaForecastingLearner(options: new SsaForecastOptions
        {
            WindowSize = 8, Horizon = 3
        });
        var model = learner.Fit(new LearnerInput(data));
        var output = model.Transform.Apply(TestHelper.SineData(10, period: 12));
        var forecasts = TestHelper.CollectFloatArray(output, "Forecast");
        // First 7 rows (windowSize-1) should have all-zero forecasts
        for (int r = 0; r < 7; r++)
            Assert.All(forecasts[r], f => Assert.Equal(0f, f));
    }

    [Fact]
    public void Fit_ForecastsAreFinite()
    {
        var data = TestHelper.SineData(50, period: 12);
        var learner = new SsaForecastingLearner(options: new SsaForecastOptions
        {
            WindowSize = 4, Horizon = 3
        });
        var model = learner.Fit(new LearnerInput(data));
        var output = model.Transform.Apply(TestHelper.SineData(20, period: 12));
        var forecasts = TestHelper.CollectFloatArray(output, "Forecast");
        Assert.All(forecasts, f => Assert.All(f, v => Assert.True(float.IsFinite(v))));
    }

    [Fact]
    public void Progress_Completes()
    {
        var data = TestHelper.SineData(50);
        var learner = new SsaForecastingLearner(options: new SsaForecastOptions { WindowSize = 4 });
        var observer = new Observer<ProgressEvent>(_ => { });
        learner.Progress.Subscribe(observer);
        learner.Fit(new LearnerInput(data));
        Assert.True(observer.Completed);
    }
}
