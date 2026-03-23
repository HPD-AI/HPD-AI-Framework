namespace HPD.ML.Evaluation.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;

public class PermutationFeatureImportanceTests
{
    /// <summary>Model that uses F1 as score (ignores F2).</summary>
    private sealed class F1OnlyModel : IModel
    {
        public ITransform Transform { get; } = new F1ScoreTransform();
        public ILearnedParameters Parameters { get; } = new EmptyParams();
    }

    private sealed class EmptyParams : ILearnedParameters;

    private sealed class F1ScoreTransform : ITransform
    {
        public TransformProperties Properties => new();

        public ISchema GetOutputSchema(ISchema inputSchema)
            => inputSchema.MergeHorizontal(
                new SchemaBuilder().AddColumn<double>("Score").Build(),
                ConflictPolicy.LastWriterWins);

        public IDataHandle Apply(IDataHandle input)
        {
            var labels = new List<double>();
            var f1Vals = new List<double>();

            using var cursor = input.GetCursor(["Label", "F1"]);
            while (cursor.MoveNext())
            {
                labels.Add(MetricHelpers.ToDouble(cursor.Current, "Label"));
                f1Vals.Add(MetricHelpers.ToDouble(cursor.Current, "F1"));
            }

            return InMemoryDataHandle.FromColumns(
                ("Label", labels.ToArray()),
                ("Score", f1Vals.ToArray()));
        }
    }

    private static IDataHandle MakeData()
    {
        var rng = new Random(42);
        int n = 20;
        var labels = new double[n];
        var f1 = new double[n];
        var f2 = new double[n];

        for (int i = 0; i < n; i++)
        {
            f1[i] = rng.NextDouble() * 10;
            f2[i] = rng.NextDouble() * 100; // noise
            labels[i] = f1[i]; // label = f1 exactly
        }

        return TestHelper.Data(
            ("Label", labels),
            ("F1", f1),
            ("F2", f2));
    }

    [Fact]
    public void Compute_ReturnsOneRowPerFeature()
    {
        var data = MakeData();
        var result = PermutationFeatureImportance.Compute(
            new F1OnlyModel(), data,
            new RegressionMetricsTransform(),
            "RSquared", ["F1", "F2"],
            permutations: 2, seed: 42);

        Assert.Equal(2, TestHelper.CountRows(result));
    }

    [Fact]
    public void Compute_OutputHasCorrectSchema()
    {
        var data = MakeData();
        var result = PermutationFeatureImportance.Compute(
            new F1OnlyModel(), data,
            new RegressionMetricsTransform(),
            "RSquared", ["F1"],
            permutations: 1, seed: 42);

        Assert.NotNull(result.Schema.FindByName("FeatureName"));
        Assert.NotNull(result.Schema.FindByName("MetricDrop"));
        Assert.NotNull(result.Schema.FindByName("MetricDropStdDev"));
    }

    [Fact]
    public void Compute_ImportantFeature_HasHigherDrop()
    {
        var data = MakeData();
        var result = PermutationFeatureImportance.Compute(
            new F1OnlyModel(), data,
            new RegressionMetricsTransform(),
            "RSquared", ["F1", "F2"],
            permutations: 3, seed: 42);

        var names = TestHelper.CollectString(result, "FeatureName");
        var drops = TestHelper.CollectDouble(result, "MetricDrop");

        int f1Idx = names.IndexOf("F1");
        int f2Idx = names.IndexOf("F2");

        Assert.True(drops[f1Idx] > drops[f2Idx],
            $"F1 drop ({drops[f1Idx]}) should be > F2 drop ({drops[f2Idx]})");
    }

    [Fact]
    public void Compute_DeterministicWithSeed()
    {
        var data = MakeData();

        var r1 = PermutationFeatureImportance.Compute(
            new F1OnlyModel(), data,
            new RegressionMetricsTransform(),
            "RSquared", ["F1"],
            permutations: 3, seed: 42);

        var r2 = PermutationFeatureImportance.Compute(
            new F1OnlyModel(), data,
            new RegressionMetricsTransform(),
            "RSquared", ["F1"],
            permutations: 3, seed: 42);

        var d1 = TestHelper.CollectDouble(r1, "MetricDrop");
        var d2 = TestHelper.CollectDouble(r2, "MetricDrop");

        Assert.Equal(d1[0], d2[0], 1e-10);
    }

    [Fact]
    public void Compute_SinglePermutation_StdDevZero()
    {
        var data = MakeData();
        var result = PermutationFeatureImportance.Compute(
            new F1OnlyModel(), data,
            new RegressionMetricsTransform(),
            "RSquared", ["F1"],
            permutations: 1, seed: 42);

        var stddevs = TestHelper.CollectDouble(result, "MetricDropStdDev");
        Assert.Equal(0.0, stddevs[0], 1e-10);
    }
}
