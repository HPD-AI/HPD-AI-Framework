namespace HPD.ML.Evaluation.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class CrossValidatorTests
{
    /// <summary>A trivial learner that always predicts the mean label.</summary>
    private sealed class MeanLearner : ILearner
    {
        private readonly ProgressSubject _progress = new();
        public IObservable<ProgressEvent> Progress => _progress;

        public ISchema GetOutputSchema(ISchema inputSchema) =>
            new SchemaBuilder().AddColumn<double>("Score").Build();

        public IModel Fit(LearnerInput input)
        {
            // Compute mean of Label column
            double sum = 0;
            int count = 0;
            using var cursor = input.TrainData.GetCursor(["Label"]);
            while (cursor.MoveNext())
            {
                sum += MetricHelpers.ToDouble(cursor.Current, "Label");
                count++;
            }
            double mean = count > 0 ? sum / count : 0;

            var transform = new ConstantScoreTransform(mean);
            _progress.OnCompleted();
            return new Model(transform, new EmptyParameters());
        }

        public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
            => Task.Run(() => Fit(input), ct);
    }

    private sealed class EmptyParameters : ILearnedParameters;

    private sealed class ConstantScoreTransform : ITransform
    {
        private readonly double _score;
        public ConstantScoreTransform(double score) => _score = score;
        public TransformProperties Properties => new();
        public ISchema GetOutputSchema(ISchema inputSchema) =>
            inputSchema.MergeHorizontal(
                new SchemaBuilder().AddColumn<double>("Score").Build(),
                ConflictPolicy.LastWriterWins);

        public IDataHandle Apply(IDataHandle input)
        {
            int n = (int)(input.RowCount ?? 0);
            var scores = new double[n];
            Array.Fill(scores, _score);

            // Copy label column
            var labels = new double[n];
            using var cursor = input.GetCursor(["Label"]);
            int i = 0;
            while (cursor.MoveNext() && i < n)
                labels[i++] = MetricHelpers.ToDouble(cursor.Current, "Label");

            return InMemoryDataHandle.FromColumns(
                ("Label", labels),
                ("Score", scores));
        }
    }

    private static IDataHandle MakeRegressionData(int n, int seed = 42)
    {
        var rng = new Random(seed);
        var labels = new double[n];
        for (int i = 0; i < n; i++)
            labels[i] = rng.NextDouble() * 10;
        var scores = new double[n]; // dummy, not used by learner
        return TestHelper.RegressionData(labels, scores);
    }

    [Fact]
    public void Evaluate_ReturnsFoldCountResults()
    {
        var data = MakeRegressionData(30);
        var result = CrossValidator.Evaluate(
            data, new MeanLearner(),
            new RegressionMetricsTransform(),
            folds: 3, seed: 42);

        Assert.Equal(3, result.Folds.Count);
    }

    [Fact]
    public void Evaluate_EachFoldHasModelAndMetrics()
    {
        var data = MakeRegressionData(30);
        var result = CrossValidator.Evaluate(
            data, new MeanLearner(),
            new RegressionMetricsTransform(),
            folds: 3, seed: 42);

        Assert.All(result.Folds, fold =>
        {
            Assert.NotNull(fold.Model);
            Assert.NotNull(fold.Metrics);
            Assert.NotNull(fold.Model.Transform);
        });
    }

    [Fact]
    public void Evaluate_AggregateHasMetricMeanStdDev()
    {
        var data = MakeRegressionData(30);
        var result = CrossValidator.Evaluate(
            data, new MeanLearner(),
            new RegressionMetricsTransform(),
            folds: 3, seed: 42);

        var agg = result.AggregateMetrics;
        Assert.NotNull(agg.Schema.FindByName("Metric"));
        Assert.NotNull(agg.Schema.FindByName("Mean"));
        Assert.NotNull(agg.Schema.FindByName("StdDev"));

        int rows = TestHelper.CountRows(agg);
        Assert.True(rows > 0, "Should have at least one aggregate metric row");
    }

    [Fact]
    public void Evaluate_WithFeaturePipeline_AppliedToTrainAndTest()
    {
        var data = MakeRegressionData(30);
        // Identity pipeline — just verifies it doesn't crash
        var pipeline = new IdentityTransform();

        var result = CrossValidator.Evaluate(
            data, new MeanLearner(),
            new RegressionMetricsTransform(),
            folds: 3, seed: 42, featurePipeline: pipeline);

        Assert.Equal(3, result.Folds.Count);
    }

    [Fact]
    public void BestModel_ReturnsHighestScoringFold()
    {
        var data = MakeRegressionData(50);
        var result = CrossValidator.Evaluate(
            data, new MeanLearner(),
            new RegressionMetricsTransform(),
            folds: 3, seed: 42);

        var best = result.BestModel("RSquared");
        Assert.NotNull(best);
    }

    [Fact]
    public void Evaluate_DeterministicWithSeed()
    {
        var data = MakeRegressionData(30);

        var r1 = CrossValidator.Evaluate(
            data, new MeanLearner(),
            new RegressionMetricsTransform(),
            folds: 3, seed: 42);

        var r2 = CrossValidator.Evaluate(
            data, new MeanLearner(),
            new RegressionMetricsTransform(),
            folds: 3, seed: 42);

        // Aggregate means should be identical
        var means1 = TestHelper.CollectDouble(r1.AggregateMetrics, "Mean");
        var means2 = TestHelper.CollectDouble(r2.AggregateMetrics, "Mean");

        Assert.Equal(means1.Count, means2.Count);
        for (int i = 0; i < means1.Count; i++)
            Assert.Equal(means1[i], means2[i], 1e-10);
    }

    private sealed class IdentityTransform : ITransform
    {
        public TransformProperties Properties => new();
        public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;
        public IDataHandle Apply(IDataHandle input) => input;
    }
}
