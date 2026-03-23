namespace HPD.ML.TimeSeries.Tests;

using HPD.ML.Abstractions;

public class SsaAnomalyLearnerTests
{
    [Fact]
    public void Fit_ReturnsSsaModelParameters()
    {
        var data = TestHelper.SineData(50, period: 12);
        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions { WindowSize = 8 });
        var model = learner.Fit(new LearnerInput(data));
        Assert.IsType<SsaModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_ParametersHaveCorrectWindowSize()
    {
        var data = TestHelper.SineData(50);
        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions { WindowSize = 8 });
        var model = learner.Fit(new LearnerInput(data));
        var p = (SsaModelParameters)model.Parameters;
        Assert.Equal(8, p.WindowSize);
    }

    [Fact]
    public void Fit_RankIsPositive()
    {
        var data = TestHelper.SineData(50);
        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions { WindowSize = 8, Rank = 0 });
        var model = learner.Fit(new LearnerInput(data));
        var p = (SsaModelParameters)model.Parameters;
        Assert.True(p.Rank >= 1, $"Rank was {p.Rank}");
    }

    [Fact]
    public void Fit_ARCoefficientsLengthIsWindowMinusOne()
    {
        var data = TestHelper.SineData(50);
        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions { WindowSize = 8 });
        var model = learner.Fit(new LearnerInput(data));
        var p = (SsaModelParameters)model.Parameters;
        Assert.Equal(7, p.AutoRegressiveCoefficients.Length);
    }

    [Fact]
    public void Fit_TransformProducesAlerts()
    {
        var data = TestHelper.SineData(50, period: 12);
        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions { WindowSize = 4 });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(TestHelper.SineData(20, period: 12));
        Assert.Equal(20, TestHelper.CountRows(predictions));
        var scores = TestHelper.CollectFloat(predictions, "RawScore");
        Assert.All(scores, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void Fit_ChangePointMode_ReturnsChangePointDetector()
    {
        var data = TestHelper.SineData(50);
        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions
        {
            WindowSize = 4, IsChangePoint = true
        });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(TestHelper.SineData(10));
        Assert.NotNull(predictions.Schema.FindByName("MartingaleScore"));
    }

    [Fact]
    public void Fit_ShortSeries_Throws()
    {
        var data = TestHelper.SineData(5);
        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions { WindowSize = 8 });
        Assert.Throws<ArgumentException>(() => learner.Fit(new LearnerInput(data)));
    }

    [Fact]
    public void Progress_Completes()
    {
        var data = TestHelper.SineData(50);
        var learner = new SsaAnomalyLearner(options: new SsaAnomalyOptions { WindowSize = 4 });
        var observer = new Observer<ProgressEvent>(_ => { });
        learner.Progress.Subscribe(observer);
        learner.Fit(new LearnerInput(data));
        Assert.True(observer.Completed);
    }
}
