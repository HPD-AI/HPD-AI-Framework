namespace HPD.ML.BinaryClassification.Tests;

using Helium.Primitives;
using HPD.ML.Abstractions;
using Double = Helium.Primitives.Double;

public class SdcaTests
{
    [Fact]
    public void Fit_LinearSeparable_Converges()
    {
        var data = TestHelper.LinearSeparableData(n: 40, seed: 42);
        var learner = new SdcaLearner(options: new SdcaOptions
        {
            NumberOfIterations = 30, Seed = 42
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var accuracy = TestHelper.Accuracy(predictions);

        Assert.True(accuracy >= 0.80, $"Accuracy was {accuracy}");
    }

    [Fact]
    public void Fit_ReturnsLinearModelParameters()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new SdcaLearner(options: new SdcaOptions { Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));

        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_WithSeed_Deterministic()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);

        var l1 = new SdcaLearner(options: new SdcaOptions { Seed = 42, NumberOfIterations = 10 });
        var l2 = new SdcaLearner(options: new SdcaOptions { Seed = 42, NumberOfIterations = 10 });

        var m1 = (LinearModelParameters)l1.Fit(new LearnerInput(data)).Parameters;
        var m2 = (LinearModelParameters)l2.Fit(new LearnerInput(data)).Parameters;

        for (int i = 0; i < m1.FeatureCount; i++)
            Assert.Equal((double)m1.Weights[i], (double)m2.Weights[i], 0.0001);
    }

    [Fact]
    public void Fit_DifferentSeeds_DifferentResults()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);

        var l1 = new SdcaLearner(options: new SdcaOptions { Seed = 1, NumberOfIterations = 5 });
        var l2 = new SdcaLearner(options: new SdcaOptions { Seed = 2, NumberOfIterations = 5 });

        var m1 = (LinearModelParameters)l1.Fit(new LearnerInput(data)).Parameters;
        var m2 = (LinearModelParameters)l2.Fit(new LearnerInput(data)).Parameters;

        bool differ = false;
        for (int i = 0; i < m1.FeatureCount; i++)
            if (Math.Abs((double)m1.Weights[i] - (double)m2.Weights[i]) > 0.001) differ = true;
        Assert.True(differ, "Different seeds should produce different weights");
    }

    [Fact]
    public void Fit_ProgressEvents_ShowLogLoss()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new SdcaLearner(options: new SdcaOptions { NumberOfIterations = 3, Seed = 42 });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count >= 1);
        Assert.All(events, e => Assert.Equal("LogLoss", e.MetricName));
    }

    [Fact]
    public void Fit_ConvergenceTolerance_EarlyStop()
    {
        // Larger dataset with higher regularization to keep updates stable
        var data = TestHelper.LinearSeparableData(n: 30, seed: 42);

        var learner = new SdcaLearner(options: new SdcaOptions
        {
            NumberOfIterations = 100, ConvergenceTolerance = 5.0, L2Regularization = 1.0, Seed = 42
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        // With high regularization and loose tolerance, should converge before 100 epochs
        Assert.True(events.Count < 100, $"Expected early stop, got {events.Count} epochs");
    }

    [Fact]
    public void Fit_HighRegularization_SmallWeights()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);

        var low = new SdcaLearner(options: new SdcaOptions { L2Regularization = 0.0001, Seed = 42 });
        var high = new SdcaLearner(options: new SdcaOptions { L2Regularization = 10, Seed = 42 });

        var lowNorm = TestHelper.WeightNorm((LinearModelParameters)low.Fit(new LearnerInput(data)).Parameters);
        var highNorm = TestHelper.WeightNorm((LinearModelParameters)high.Fit(new LearnerInput(data)).Parameters);

        Assert.True(highNorm < lowNorm, $"High reg {highNorm} should be < low reg {lowNorm}");
    }

    [Fact]
    public void Fit_SingleFeature()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f], [4f] }),
            ("Label", new bool[] { false, false, true, true }));

        var learner = new SdcaLearner(options: new SdcaOptions { Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));

        Assert.Equal(1, ((LinearModelParameters)model.Parameters).FeatureCount);
    }

    [Fact]
    public void Fit_ManyIterations_LossDecreases()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);
        var learner = new SdcaLearner(options: new SdcaOptions
        {
            NumberOfIterations = 20, Seed = 42, ConvergenceTolerance = 0
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count >= 2);
        Assert.True(events.Last().MetricValue <= events.First().MetricValue! + 0.1,
            "Loss should generally decrease");
    }

    [Fact]
    public async Task FitAsync_Works()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new SdcaLearner(options: new SdcaOptions { NumberOfIterations = 3, Seed = 42 });
        var model = await learner.FitAsync(new LearnerInput(data));
        Assert.NotNull(model);
    }

    [Fact]
    public void Fit_ApplyToNewData()
    {
        var train = TestHelper.LinearSeparableData(n: 20, seed: 42);
        var test = TestHelper.LinearSeparableData(n: 5, seed: 99);

        var learner = new SdcaLearner(options: new SdcaOptions { Seed = 42 });
        var model = learner.Fit(new LearnerInput(train));
        var predictions = model.Transform.Apply(test);

        Assert.Equal(5, TestHelper.CountRows(predictions));
    }
}
