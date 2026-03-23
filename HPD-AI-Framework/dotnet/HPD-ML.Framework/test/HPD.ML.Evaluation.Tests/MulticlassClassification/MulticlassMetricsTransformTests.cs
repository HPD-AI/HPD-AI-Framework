namespace HPD.ML.Evaluation.Tests;

public class MulticlassMetricsTransformTests
{
    [Fact]
    public void Apply_PerfectPredictions_FullAccuracy()
    {
        var data = TestHelper.MulticlassData(
            labels: [0, 1, 2],
            predicted: [0, 1, 2]);

        var transform = new MulticlassMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "MicroAccuracy"), 1e-10);
        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "MacroAccuracy"), 1e-10);
    }

    [Fact]
    public void Apply_AllWrong_ZeroAccuracy()
    {
        var data = TestHelper.MulticlassData(
            labels: [0, 1, 2],
            predicted: [1, 2, 0]);

        var transform = new MulticlassMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "MicroAccuracy"), 1e-10);
        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "MacroAccuracy"), 1e-10);
    }

    [Fact]
    public void Apply_ImbalancedClasses_MacroDiffersFromMicro()
    {
        // 4 class-0, 1 class-1; predict all as 0
        var data = TestHelper.MulticlassData(
            labels: [0, 0, 0, 0, 1],
            predicted: [0, 0, 0, 0, 0]);

        var transform = new MulticlassMetricsTransform();
        var metrics = transform.Apply(data);

        double micro = TestHelper.ReadMetric(metrics, "MicroAccuracy");
        double macro = TestHelper.ReadMetric(metrics, "MacroAccuracy");

        Assert.Equal(4.0 / 5.0, micro, 1e-10); // 4 correct out of 5
        Assert.Equal(0.5, macro, 1e-10); // (1.0 + 0.0) / 2
        Assert.True(macro < micro);
    }

    [Fact]
    public void Apply_WithScoreVectors_ComputesLogLoss()
    {
        var data = TestHelper.MulticlassData(
            labels: [0, 1],
            predicted: [0, 1],
            scores: [[0.9f, 0.1f], [0.2f, 0.8f]]);

        var transform = new MulticlassMetricsTransform();
        var metrics = transform.Apply(data);

        double logLoss = TestHelper.ReadMetric(metrics, "LogLoss");
        Assert.True(logLoss > 0, $"LogLoss should be > 0, got {logLoss}");
        Assert.True(double.IsFinite(logLoss));
    }

    [Fact]
    public void Apply_WithoutScoreVectors_LogLossZero()
    {
        // Pass null score arrays
        var data = TestHelper.Data(
            ("Label", new int[] { 0, 1 }),
            ("PredictedLabel", new int[] { 0, 1 }),
            ("Score", new int[] { 0, 0 })); // not float[], won't parse

        var transform = new MulticlassMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "LogLoss"), 1e-10);
    }

    [Fact]
    public void GetOutputSchema_HasAllColumns()
    {
        var transform = new MulticlassMetricsTransform();
        var schema = transform.GetOutputSchema(
            new HPD.ML.Core.SchemaBuilder().AddColumn<float>("dummy").Build());

        foreach (var name in new[] { "MicroAccuracy", "MacroAccuracy", "LogLoss", "LogLossReduction" })
            Assert.NotNull(schema.FindByName(name));
    }

    [Fact]
    public void Apply_SingleClass_HandlesGracefully()
    {
        var data = TestHelper.MulticlassData(
            labels: [1, 1, 1],
            predicted: [1, 1, 1]);

        var transform = new MulticlassMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "MicroAccuracy"), 1e-10);
    }
}
