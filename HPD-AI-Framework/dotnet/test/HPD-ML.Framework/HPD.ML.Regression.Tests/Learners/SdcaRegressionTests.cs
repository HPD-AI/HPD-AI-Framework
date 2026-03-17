namespace HPD.ML.Regression.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public class SdcaRegressionTests
{
    [Fact]
    public void Fit_LinearData_LowMSE()
    {
        var data = TestHelper.LinearData(slope: 2, intercept: 1, noise: 0.1, n: 30, seed: 42);
        var learner = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            NumberOfIterations = 50, L2Regularization = 0.1, Seed = 42
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var mse = TestHelper.MSE(predictions);

        Assert.True(mse < 5.0, $"MSE was {mse}");
    }

    [Fact]
    public void Fit_ReturnsLinearModelParameters()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new SdcaRegressionLearner(options: new SdcaRegressionOptions { Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));
        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_WithSeed_Deterministic()
    {
        var data = TestHelper.LinearData(n: 20, seed: 42);

        var l1 = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            Seed = 42, NumberOfIterations = 10
        });
        var l2 = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            Seed = 42, NumberOfIterations = 10
        });

        var m1 = (LinearModelParameters)l1.Fit(new LearnerInput(data)).Parameters;
        var m2 = (LinearModelParameters)l2.Fit(new LearnerInput(data)).Parameters;

        for (int i = 0; i < m1.FeatureCount; i++)
            Assert.Equal((double)m1.Weights[i], (double)m2.Weights[i], 0.0001);
    }

    [Fact]
    public void Fit_HighRegularization_SmallWeights()
    {
        var data = TestHelper.LinearData(n: 20, seed: 42);

        var low = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            L2Regularization = 0.001, NumberOfIterations = 20, Seed = 42
        });
        var high = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            L2Regularization = 10, NumberOfIterations = 20, Seed = 42
        });

        var lowNorm = TestHelper.WeightNorm((LinearModelParameters)low.Fit(new LearnerInput(data)).Parameters);
        var highNorm = TestHelper.WeightNorm((LinearModelParameters)high.Fit(new LearnerInput(data)).Parameters);

        Assert.True(highNorm < lowNorm, $"High reg norm {highNorm} should be < low reg norm {lowNorm}");
    }

    [Fact]
    public void Fit_ConvergenceTolerance_EarlyStop()
    {
        var data = TestHelper.LinearData(n: 30, seed: 42);
        var learner = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            NumberOfIterations = 100, ConvergenceTolerance = 5.0, L2Regularization = 1.0, Seed = 42
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count < 100, $"Expected early stop, got {events.Count} epochs");
    }

    [Fact]
    public void Fit_ProgressReports_SquaredLoss()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            NumberOfIterations = 3, Seed = 42
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count >= 1);
        Assert.All(events, e => Assert.Equal("SquaredLoss", e.MetricName));
    }

    [Fact]
    public void Fit_SingleFeature()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f], [4f] }),
            ("Label", new float[] { 2f, 4f, 6f, 8f }));

        var learner = new SdcaRegressionLearner(options: new SdcaRegressionOptions { Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));
        Assert.Equal(1, ((LinearModelParameters)model.Parameters).FeatureCount);
    }

    [Fact]
    public async Task FitAsync_Works()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            NumberOfIterations = 3, Seed = 42
        });
        var model = await learner.FitAsync(new LearnerInput(data));
        Assert.NotNull(model);
    }

    [Fact]
    public void Fit_PredictionsReasonable()
    {
        // y = 2x + 1
        var data = TestHelper.LinearData(slope: 2, intercept: 1, noise: 0, n: 20, seed: 42);
        var learner = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            NumberOfIterations = 50, L2Regularization = 0.1, Seed = 42
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var mse = TestHelper.MSE(predictions);

        Assert.True(mse < 5.0, $"MSE was {mse}");
    }

    [Fact]
    public void Fit_LossDecreases()
    {
        var data = TestHelper.LinearData(n: 30, seed: 42);
        var learner = new SdcaRegressionLearner(options: new SdcaRegressionOptions
        {
            NumberOfIterations = 10, L2Regularization = 0.1, Seed = 42
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count >= 2);
        Assert.True(events.Last().MetricValue <= events.First().MetricValue! + 0.5,
            $"Loss should decrease: first={events.First().MetricValue}, last={events.Last().MetricValue}");
    }
}
