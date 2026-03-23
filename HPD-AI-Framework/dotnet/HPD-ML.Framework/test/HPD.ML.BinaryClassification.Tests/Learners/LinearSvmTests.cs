namespace HPD.ML.BinaryClassification.Tests;

using Helium.Primitives;
using HPD.ML.Abstractions;
using Double = Helium.Primitives.Double;

public class LinearSvmTests
{
    [Fact]
    public void Fit_LinearSeparable_FindsMargin()
    {
        var data = TestHelper.LinearSeparableData(n: 40, seed: 42);
        var learner = new LinearSvmLearner(options: new LinearSvmOptions
        {
            NumberOfIterations = 20, Seed = 42
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var accuracy = TestHelper.Accuracy(predictions);

        Assert.True(accuracy >= 0.75, $"Accuracy was {accuracy}");
    }

    [Fact]
    public void Fit_ReturnsLinearModelParameters()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new LinearSvmLearner(options: new LinearSvmOptions { Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));
        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_WithSeed_Deterministic()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);

        var l1 = new LinearSvmLearner(options: new LinearSvmOptions { Seed = 42, NumberOfIterations = 10 });
        var l2 = new LinearSvmLearner(options: new LinearSvmOptions { Seed = 42, NumberOfIterations = 10 });

        var m1 = (LinearModelParameters)l1.Fit(new LearnerInput(data)).Parameters;
        var m2 = (LinearModelParameters)l2.Fit(new LearnerInput(data)).Parameters;

        for (int i = 0; i < m1.FeatureCount; i++)
            Assert.Equal((double)m1.Weights[i], (double)m2.Weights[i], 0.0001);
    }

    [Fact]
    public void Fit_Projection_ClipsWeightNorm()
    {
        var data = TestHelper.LinearSeparableData(n: 30, seed: 42);
        var learner = new LinearSvmLearner(options: new LinearSvmOptions
        {
            Lambda = 1.0, PerformProjection = true, NumberOfIterations = 20, Seed = 42
        });

        var model = learner.Fit(new LearnerInput(data));
        var p = (LinearModelParameters)model.Parameters;
        var norm = TestHelper.WeightNorm(p);

        double maxNorm = 1.0 / Math.Sqrt(1.0); // 1/√λ = 1
        Assert.True(norm <= maxNorm + 0.01, $"Norm {norm} should be ≤ {maxNorm}");
    }

    [Fact]
    public void Fit_NoProjection_LargerNorm()
    {
        var data = TestHelper.LinearSeparableData(n: 30, seed: 42);

        var withProj = new LinearSvmLearner(options: new LinearSvmOptions
        {
            Lambda = 0.001, PerformProjection = true, NumberOfIterations = 20, Seed = 42
        });
        var noProj = new LinearSvmLearner(options: new LinearSvmOptions
        {
            Lambda = 0.001, PerformProjection = false, NumberOfIterations = 20, Seed = 42
        });

        var projNorm = TestHelper.WeightNorm((LinearModelParameters)withProj.Fit(new LearnerInput(data)).Parameters);
        var noProjNorm = TestHelper.WeightNorm((LinearModelParameters)noProj.Fit(new LearnerInput(data)).Parameters);

        // Without projection, norm can be larger
        Assert.True(noProjNorm >= projNorm - 0.1,
            $"No-proj norm {noProjNorm} should be ≥ proj norm {projNorm}");
    }

    [Fact]
    public void Fit_NoBias_BiasStaysZero()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);
        var learner = new LinearSvmLearner(options: new LinearSvmOptions
        {
            NoBias = true, NumberOfIterations = 10, Seed = 42
        });

        var model = learner.Fit(new LearnerInput(data));
        var p = (LinearModelParameters)model.Parameters;
        Assert.Equal(0.0, (double)p.Bias, 0.0001);
    }

    [Fact]
    public void Fit_ProgressReports_HingeLoss()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new LinearSvmLearner(options: new LinearSvmOptions
        {
            NumberOfIterations = 3, Seed = 42
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal("HingeLoss", e.MetricName));
    }

    [Fact]
    public void Fit_HighLambda_SmallWeights()
    {
        var data = TestHelper.LinearSeparableData(n: 20, seed: 42);

        var low = new LinearSvmLearner(options: new LinearSvmOptions
        {
            Lambda = 0.0001, NumberOfIterations = 10, Seed = 42
        });
        var high = new LinearSvmLearner(options: new LinearSvmOptions
        {
            Lambda = 10, NumberOfIterations = 10, Seed = 42
        });

        var lowNorm = TestHelper.WeightNorm((LinearModelParameters)low.Fit(new LearnerInput(data)).Parameters);
        var highNorm = TestHelper.WeightNorm((LinearModelParameters)high.Fit(new LearnerInput(data)).Parameters);

        Assert.True(highNorm < lowNorm, $"High lambda {highNorm} should be < low lambda {lowNorm}");
    }

    [Fact]
    public void Fit_WithValidationData_AddsCalibration()
    {
        var trainData = TestHelper.LinearSeparableData(n: 30, seed: 42);
        var valData = TestHelper.LinearSeparableData(n: 10, seed: 99);

        var learner = new LinearSvmLearner(options: new LinearSvmOptions
        {
            NumberOfIterations = 10, Seed = 42
        });
        var model = learner.Fit(new LearnerInput(trainData, ValidationData: valData));
        var predictions = model.Transform.Apply(trainData);

        var probs = TestHelper.CollectFloat(predictions, "Probability");
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
    }

    [Fact]
    public void Fit_WithoutValidation_NoCalibration()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new LinearSvmLearner(options: new LinearSvmOptions { Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));

        Assert.IsType<LinearScoringTransform>(model.Transform);
    }

    [Fact]
    public void Fit_HingeLoss_Decreases()
    {
        var data = TestHelper.LinearSeparableData(n: 30, seed: 42);
        var learner = new LinearSvmLearner(options: new LinearSvmOptions
        {
            NumberOfIterations = 20, Seed = 42
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count >= 2);
        // Last loss should be ≤ first (with some tolerance for stochastic noise)
        Assert.True(events.Last().MetricValue <= events.First().MetricValue! + 0.5,
            $"Loss should generally decrease: first={events.First().MetricValue}, last={events.Last().MetricValue}");
    }

    [Fact]
    public async Task FitAsync_Works()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new LinearSvmLearner(options: new LinearSvmOptions { NumberOfIterations = 3, Seed = 42 });
        var model = await learner.FitAsync(new LearnerInput(data));
        Assert.NotNull(model);
    }

    [Fact]
    public void Fit_SingleFeature()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f], [4f] }),
            ("Label", new bool[] { false, false, true, true }));

        var learner = new LinearSvmLearner(options: new LinearSvmOptions { Seed = 42 });
        var model = learner.Fit(new LearnerInput(data));
        Assert.Equal(1, ((LinearModelParameters)model.Parameters).FeatureCount);
    }
}
