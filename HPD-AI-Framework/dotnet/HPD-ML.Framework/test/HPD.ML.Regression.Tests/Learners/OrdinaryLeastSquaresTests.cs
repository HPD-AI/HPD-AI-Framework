namespace HPD.ML.Regression.Tests;

using Helium.Algebra;
using Helium.Primitives;
using HPD.ML.Abstractions;
using HPD.ML.BinaryClassification;
using HPD.ML.Core;
using Double = Helium.Primitives.Double;

public class OrdinaryLeastSquaresTests
{
    [Fact]
    public void Fit_LinearData_LowMSE()
    {
        var data = TestHelper.LinearData(slope: 2, intercept: 1, noise: 0.1, n: 30, seed: 42);
        var learner = new OrdinaryLeastSquaresLearner(options: new OlsOptions
        {
            MaxIterations = 100, L2Regularization = 0.01f
        });

        var model = learner.Fit(new LearnerInput(data));
        var predictions = model.Transform.Apply(data);
        var mse = TestHelper.MSE(predictions);

        Assert.True(mse < 1.0, $"MSE was {mse}");
    }

    [Fact]
    public void Fit_ReturnsLinearModelParameters()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new OrdinaryLeastSquaresLearner();
        var model = learner.Fit(new LearnerInput(data));
        Assert.IsType<LinearModelParameters>(model.Parameters);
    }

    [Fact]
    public void Fit_WeightsApproximateTrue()
    {
        // y = 3x + 2 (no noise)
        var data = TestHelper.LinearData(slope: 3, intercept: 2, noise: 0, n: 20, seed: 42);
        var learner = new OrdinaryLeastSquaresLearner(options: new OlsOptions
        {
            MaxIterations = 200, L2Regularization = 0.001f
        });

        var model = learner.Fit(new LearnerInput(data));
        var p = (LinearModelParameters)model.Parameters;

        Assert.Equal(3.0, (double)p.Weights[0], 0.5);
        Assert.Equal(2.0, (double)p.Bias, 0.5);
    }

    [Fact]
    public void Fit_L2Regularization_SmallWeights()
    {
        var data = TestHelper.LinearData(slope: 2, intercept: 1, n: 30, seed: 42);

        var low = new OrdinaryLeastSquaresLearner(options: new OlsOptions
        {
            L2Regularization = 0.01f, MaxIterations = 50
        });
        var high = new OrdinaryLeastSquaresLearner(options: new OlsOptions
        {
            L2Regularization = 10f, MaxIterations = 50
        });

        var lowNorm = TestHelper.WeightNorm((LinearModelParameters)low.Fit(new LearnerInput(data)).Parameters);
        var highNorm = TestHelper.WeightNorm((LinearModelParameters)high.Fit(new LearnerInput(data)).Parameters);

        Assert.True(highNorm < lowNorm, $"High reg norm {highNorm} should be < low reg norm {lowNorm}");
    }

    [Fact]
    public void Fit_L1Regularization_SparseWeights()
    {
        var rng = new Random(42);
        int n = 30;
        var features = new float[n][];
        var labels = new float[n];
        for (int i = 0; i < n; i++)
        {
            float x = (float)rng.NextDouble();
            features[i] = [x, (float)rng.NextDouble(), (float)rng.NextDouble()];
            labels[i] = 2f * x + 1f;
        }
        var data = TestHelper.Data(("Features", features), ("Label", labels));

        var withL1 = new OrdinaryLeastSquaresLearner(options: new OlsOptions
        {
            L1Regularization = 2f, L2Regularization = 5f, MaxIterations = 50
        });
        var withoutL1 = new OrdinaryLeastSquaresLearner(options: new OlsOptions
        {
            L1Regularization = 0f, L2Regularization = 5f, MaxIterations = 50
        });

        var normWith = TestHelper.WeightNorm((LinearModelParameters)withL1.Fit(new LearnerInput(data)).Parameters);
        var normWithout = TestHelper.WeightNorm((LinearModelParameters)withoutL1.Fit(new LearnerInput(data)).Parameters);
        Assert.True(normWith <= normWithout + 0.01,
            $"L1 norm {normWith} should be ≤ no-L1 norm {normWithout}");
    }

    [Fact]
    public void Fit_ProgressReports_Loss()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new OrdinaryLeastSquaresLearner(options: new OlsOptions { MaxIterations = 5 });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count >= 1);
        Assert.All(events, e => Assert.Equal("Loss", e.MetricName));
    }

    [Fact]
    public void Fit_MaxIterations_Respected()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new OrdinaryLeastSquaresLearner(options: new OlsOptions { MaxIterations = 3 });

        var events = new List<ProgressEvent>();
        learner.Progress.Subscribe(new Observer<ProgressEvent>(events.Add));
        learner.Fit(new LearnerInput(data));

        Assert.True(events.Count <= 3, $"Expected ≤3 events, got {events.Count}");
    }

    [Fact]
    public void Fit_SingleFeature()
    {
        var data = TestHelper.Data(
            ("Features", new float[][] { [1f], [2f], [3f], [4f] }),
            ("Label", new float[] { 2f, 4f, 6f, 8f }));

        var learner = new OrdinaryLeastSquaresLearner();
        var model = learner.Fit(new LearnerInput(data));
        Assert.Equal(1, ((LinearModelParameters)model.Parameters).FeatureCount);
    }

    [Fact]
    public async Task FitAsync_Works()
    {
        var data = TestHelper.Simple1DData(n: 10);
        var learner = new OrdinaryLeastSquaresLearner(options: new OlsOptions { MaxIterations = 5 });
        var model = await learner.FitAsync(new LearnerInput(data));
        Assert.NotNull(model);
    }

    [Fact]
    public void GetOutputSchema_IncludesScoreColumn()
    {
        var learner = new OrdinaryLeastSquaresLearner();
        var inputSchema = new SchemaBuilder()
            .AddColumn<float>("Features")
            .AddColumn<float>("Label")
            .Build();

        var outputSchema = learner.GetOutputSchema(inputSchema);
        Assert.NotNull(outputSchema.FindByName("Score"));
    }
}
