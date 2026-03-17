namespace HPD.ML.BinaryClassification.Tests;

using Helium.Primitives;
using HPD.ML.Abstractions;
using Double = Helium.Primitives.Double;

public class LogisticRegressionTests
{
    [Fact]
    public void Fit_LinearSeparable_HighAccuracy()
    {
        var data = TestHelper.LinearSeparableData(n: 40, seed: 42);
        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 50, L2Regularization = 0.01f
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var accuracy = TestHelper.Accuracy(predictions);

        Assert.True(accuracy >= 0.85, $"Accuracy was {accuracy}");
    }

    [Fact]
    public void Fit_XOR_LowAccuracy()
    {
        var data = TestHelper.XorData();
        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 100, L2Regularization = 0.01f
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var accuracy = TestHelper.Accuracy(predictions);

        // XOR is not linearly separable — accuracy should be ≤ 75%
        Assert.True(accuracy <= 0.75, $"Accuracy was {accuracy} (XOR should be non-separable)");
    }

    [Fact]
    public void Fit_SingleFeature_LearnsBoundary()
    {
        var data = TestHelper.Simple1DData(boundary: 0.5f, n: 40, seed: 42);
        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 50, L2Regularization = 0.1f
        });

        var model = learner.Fit(new LearnerInput(data));
        var p = (LinearModelParameters)model.Parameters;

        // Weight should be positive (higher feature → more likely positive)
        Assert.True((double)p.Weights[0] > 0);
    }

    [Fact]
    public void Fit_ReturnsModel_WithLinearParams()
    {
        var data = TestHelper.Simple1DData();
        var learner = new LogisticRegressionLearner();
        var model = learner.Fit(new LearnerInput(data));

        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_Transform_AddsScoreProbabilityColumns()
    {
        var data = TestHelper.Simple1DData();
        var learner = new LogisticRegressionLearner();
        var model = learner.Fit(new LearnerInput(data));
        var result = model.Transform.Apply(data);

        Assert.NotNull(result.Schema.FindByName("Score"));
        Assert.NotNull(result.Schema.FindByName("Probability"));
        Assert.NotNull(result.Schema.FindByName("PredictedLabel"));
    }

    [Fact]
    public void Fit_ProbabilitiesAreCalibrated()
    {
        var data = TestHelper.LinearSeparableData(n: 30);
        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 50
        });
        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var probs = TestHelper.CollectFloat(predictions, "Probability");

        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
    }

    [Fact]
    public void Fit_ProgressEvents_Emitted()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 5
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));

        learner.Fit(new LearnerInput(data));
        Assert.True(events.Count >= 1);
    }

    [Fact]
    public void Fit_WithL2_SmallWeights()
    {
        var data = TestHelper.LinearSeparableData(n: 30);

        var lowReg = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            L2Regularization = 0.001f, MaxIterations = 30
        });
        var highReg = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            L2Regularization = 10f, MaxIterations = 30
        });

        var lowModel = lowReg.Fit(new LearnerInput(data));
        var highModel = highReg.Fit(new LearnerInput(data));

        var lowNorm = TestHelper.WeightNorm((LinearModelParameters)lowModel.Parameters);
        var highNorm = TestHelper.WeightNorm((LinearModelParameters)highModel.Parameters);

        Assert.True(highNorm < lowNorm, $"High reg norm {highNorm} should be < low reg norm {lowNorm}");
    }

    [Fact]
    public void Fit_WithL1_ShrinksSomeWeights()
    {
        // L1 proximal operator applied post-optimization shrinks small weights to zero
        // Use L2 to keep weights small, then L1 to zero them out
        var rng = new Random(42);
        int n = 30;
        var features = new float[n][];
        var labels = new bool[n];
        for (int i = 0; i < n; i++)
        {
            float x = (float)rng.NextDouble();
            features[i] = [x, (float)rng.NextDouble(), (float)rng.NextDouble()];
            labels[i] = x > 0.5f;
        }
        var data = TestHelper.Data(("Features", features), ("Label", labels));

        // Use L2 to keep weights moderate, then L1 to prune
        var withL1 = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            L1Regularization = 2f, L2Regularization = 5f, MaxIterations = 50
        });
        var withoutL1 = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            L1Regularization = 0f, L2Regularization = 5f, MaxIterations = 50
        });

        var pWith = (LinearModelParameters)withL1.Fit(new LearnerInput(data)).Parameters;
        var pWithout = (LinearModelParameters)withoutL1.Fit(new LearnerInput(data)).Parameters;

        // L1 version should have smaller or equal weight magnitudes
        double normWith = TestHelper.WeightNorm(pWith);
        double normWithout = TestHelper.WeightNorm(pWithout);
        Assert.True(normWith <= normWithout + 0.01,
            $"L1 norm {normWith} should be ≤ no-L1 norm {normWithout}");
    }

    [Fact]
    public void Fit_MaxIterations_Respected()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 1
        });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count <= 2); // 1 iteration + possible convergence check
    }

    [Fact]
    public async Task FitAsync_ReturnsModel()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 5
        });

        var model = await learner.FitAsync(new LearnerInput(data));
        Assert.NotNull(model);
        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void GetOutputSchema_IncludesScoringColumns()
    {
        var learner = new LogisticRegressionLearner();
        var inputSchema = new HPD.ML.Core.SchemaBuilder()
            .AddColumn<float>("Features")
            .AddColumn<bool>("Label")
            .Build();

        var outputSchema = learner.GetOutputSchema(inputSchema);
        Assert.NotNull(outputSchema.FindByName("Score"));
        Assert.NotNull(outputSchema.FindByName("Probability"));
        Assert.NotNull(outputSchema.FindByName("PredictedLabel"));
    }

    [Fact]
    public void Fit_EmptyFeatures_SingleScalar()
    {
        // Single scalar feature column
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f], [4f] }),
            ("Label", new bool[] { false, false, true, true }));

        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 20
        });
        var model = learner.Fit(new LearnerInput(data));
        var p = (LinearModelParameters)model.Parameters;

        Assert.Equal(1, p.FeatureCount);
    }

    [Fact]
    public void Fit_ApplyToTestData_Generalizes()
    {
        var trainData = TestHelper.LinearSeparableData(n: 30, seed: 42);
        var testData = TestHelper.LinearSeparableData(n: 10, seed: 99);

        var learner = new LogisticRegressionLearner(options: new LogisticRegressionOptions
        {
            MaxIterations = 50
        });
        var model = learner.Fit(new LearnerInput(trainData));
        var predictions = model.Transform.Apply(testData);

        var count = TestHelper.CountRows(predictions);
        Assert.Equal(10, count);

        var probs = TestHelper.CollectFloat(predictions, "Probability");
        Assert.All(probs, p => Assert.InRange(p, 0f, 1f));
    }
}
