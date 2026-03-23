namespace HPD.ML.Evaluation.Tests;

public class RegressionMetricsTransformTests
{
    [Fact]
    public void Apply_PerfectPredictions_ZeroError()
    {
        var data = TestHelper.RegressionData(
            labels: [1.0, 2.0, 3.0],
            scores: [1.0, 2.0, 3.0]);

        var transform = new RegressionMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "MAE"), 1e-10);
        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "MSE"), 1e-10);
        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "RMSE"), 1e-10);
        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "RSquared"), 1e-10);
    }

    [Fact]
    public void Apply_ConstantOffset_CorrectMAE()
    {
        var data = TestHelper.RegressionData(
            labels: [10.0, 20.0, 30.0],
            scores: [12.0, 22.0, 32.0]);

        var transform = new RegressionMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(2.0, TestHelper.ReadMetric(metrics, "MAE"), 1e-10);
        Assert.Equal(4.0, TestHelper.ReadMetric(metrics, "MSE"), 1e-10);
        Assert.Equal(2.0, TestHelper.ReadMetric(metrics, "RMSE"), 1e-10);
    }

    [Fact]
    public void Apply_RSquared_NegativeForBadModel()
    {
        var data = TestHelper.RegressionData(
            labels: [1.0, 2.0, 3.0],
            scores: [10.0, 10.0, 10.0]);

        var transform = new RegressionMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.True(TestHelper.ReadMetric(metrics, "RSquared") < 0);
    }

    [Fact]
    public void Apply_AdjustedRSquared_WithFeatureCount()
    {
        var data = TestHelper.RegressionData(
            labels: [1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0],
            scores: [1.1, 2.1, 2.9, 4.1, 5.0, 5.9, 7.1, 8.0, 8.9, 10.1]);

        var transform = new RegressionMetricsTransform(featureCount: 5);
        var metrics = transform.Apply(data);

        double r2 = TestHelper.ReadMetric(metrics, "RSquared");
        double adjR2 = TestHelper.ReadMetric(metrics, "AdjustedRSquared");
        Assert.True(adjR2 < r2, $"AdjustedR²={adjR2} should be < R²={r2}");
    }

    [Fact]
    public void Apply_AdjustedRSquared_WithoutFeatureCount_EqualToRSquared()
    {
        var data = TestHelper.RegressionData(
            labels: [1.0, 2.0, 3.0],
            scores: [1.1, 2.1, 2.9]);

        var transform = new RegressionMetricsTransform();
        var metrics = transform.Apply(data);

        double r2 = TestHelper.ReadMetric(metrics, "RSquared");
        double adjR2 = TestHelper.ReadMetric(metrics, "AdjustedRSquared");
        Assert.Equal(r2, adjR2, 1e-10);
    }

    [Fact]
    public void Apply_SingleDataPoint_HandlesGracefully()
    {
        var data = TestHelper.RegressionData(labels: [5.0], scores: [3.0]);

        var transform = new RegressionMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(2.0, TestHelper.ReadMetric(metrics, "MAE"), 1e-10);
        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "RSquared"), 1e-10);
    }

    [Fact]
    public void GetOutputSchema_HasAllMetricColumns()
    {
        var transform = new RegressionMetricsTransform();
        var schema = transform.GetOutputSchema(
            new HPD.ML.Core.SchemaBuilder().AddColumn<float>("dummy").Build());

        foreach (var name in new[] { "MAE", "MSE", "RMSE", "RSquared", "AdjustedRSquared" })
            Assert.NotNull(schema.FindByName(name));
    }

    [Fact]
    public void Apply_LargeErrors_FiniteResults()
    {
        var data = TestHelper.RegressionData(labels: [1e6], scores: [-1e6]);

        var transform = new RegressionMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.True(double.IsFinite(TestHelper.ReadMetric(metrics, "MAE")));
        Assert.True(double.IsFinite(TestHelper.ReadMetric(metrics, "MSE")));
        Assert.True(double.IsFinite(TestHelper.ReadMetric(metrics, "RMSE")));
    }
}
