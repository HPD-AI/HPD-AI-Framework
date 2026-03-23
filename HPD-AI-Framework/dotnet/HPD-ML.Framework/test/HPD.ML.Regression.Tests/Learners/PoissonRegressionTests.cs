namespace HPD.ML.Regression.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public class PoissonRegressionTests
{
    [Fact]
    public void Fit_CountData_NonNegativePredictions()
    {
        var data = TestHelper.CountData(n: 30, seed: 42);
        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions
        {
            MaxIterations = 50, L2Regularization = 0.1f
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var scores = TestHelper.CollectFloat(predictions, "Score");

        Assert.Equal(30, scores.Count);
        Assert.All(scores, s => Assert.True(s > 0, $"Score {s} should be > 0"));
    }

    [Fact]
    public void Fit_ReturnsLinearModelParameters()
    {
        var data = TestHelper.CountData(n: 10, seed: 42);
        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions { MaxIterations = 10 });
        var model = learner.Fit(new LearnerInput(data));
        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_NegativeLabels_Throws()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f] }),
            ("Label", new float[] { 1f, -1f }));

        var learner = new PoissonRegressionLearner();
        Assert.Throws<ArgumentException>(() => learner.Fit(new LearnerInput(data)));
    }

    [Fact]
    public void Fit_HigherFeatures_HigherCounts()
    {
        // Monotonic: higher x → higher count
        var features = new float[10][];
        var labels = new float[10];
        for (int i = 0; i < 10; i++)
        {
            features[i] = [(float)i];
            labels[i] = (float)(i + 1); // 1..10
        }
        var data = TestHelper.Data(("Features", features), ("Label", labels));

        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions
        {
            MaxIterations = 100, L2Regularization = 0.01f
        });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var scores = TestHelper.CollectFloat(predictions, "Score");

        // Predictions should be monotonically increasing
        for (int i = 1; i < scores.Count; i++)
            Assert.True(scores[i] >= scores[i - 1] - 0.1f,
                $"Score[{i}]={scores[i]} should be >= Score[{i - 1}]={scores[i - 1]}");
    }

    [Fact]
    public void Fit_L2Regularization_SmallWeights()
    {
        var data = TestHelper.CountData(n: 20, seed: 42);

        var low = new PoissonRegressionLearner(options: new PoissonRegressionOptions
        {
            L2Regularization = 0.1f, MaxIterations = 50
        });
        var high = new PoissonRegressionLearner(options: new PoissonRegressionOptions
        {
            L2Regularization = 10f, MaxIterations = 50
        });

        var lowNorm = TestHelper.WeightNorm((LinearModelParameters)low.Fit(new LearnerInput(data)).Parameters);
        var highNorm = TestHelper.WeightNorm((LinearModelParameters)high.Fit(new LearnerInput(data)).Parameters);

        Assert.True(highNorm < lowNorm, $"High reg norm {highNorm} should be < low reg norm {lowNorm}");
    }

    [Fact]
    public void Fit_ProgressReports_Loss()
    {
        var data = TestHelper.CountData(n: 10, seed: 42);
        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions { MaxIterations = 5 });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count >= 1);
        Assert.All(events, e => Assert.Equal("Loss", e.MetricName));
    }

    [Fact]
    public void Fit_ScoringTransformAppliesExp()
    {
        var data = TestHelper.CountData(n: 10, seed: 42);
        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions { MaxIterations = 10 });
        var model = learner.Fit(new LearnerInput(data));

        Assert.IsType<RegressionScoringTransform>(model.Transform);
    }

    [Fact]
    public void Fit_ZeroLabels_HandledGracefully()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [0f], [1f], [2f] }),
            ("Label", new float[] { 0f, 0f, 0f }));

        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions
        {
            MaxIterations = 20, L2Regularization = 1f
        });
        var model = learner.Fit(new LearnerInput(data));
        Assert.NotNull(model);

        var predictions = model.Transform.Apply(data);
        var scores = TestHelper.CollectFloat(predictions, "Score");
        Assert.All(scores, s => Assert.True(s > 0, $"Score {s} should be > 0"));
    }

    [Fact]
    public async Task FitAsync_Works()
    {
        var data = TestHelper.CountData(n: 10, seed: 42);
        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions { MaxIterations = 5 });
        var model = await learner.FitAsync(new LearnerInput(data));
        Assert.NotNull(model);
    }

    [Fact]
    public void Fit_SingleFeature()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f], [4f] }),
            ("Label", new float[] { 1f, 2f, 3f, 4f }));

        var learner = new PoissonRegressionLearner(options: new PoissonRegressionOptions { MaxIterations = 10 });
        var model = learner.Fit(new LearnerInput(data));
        Assert.Equal(1, ((LinearModelParameters)model.Parameters).FeatureCount);
    }
}
