namespace HPD.ML.Evaluation.Tests;

public class BinaryClassificationMetricsTransformTests
{
    [Fact]
    public void Apply_PerfectClassifier_ReturnsIdealMetrics()
    {
        var data = TestHelper.BinaryData(
            labels: [true, true, false, false],
            scores: [0.9, 0.8, 0.1, 0.2]);

        var transform = new BinaryClassificationMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "AUC"));
        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "Accuracy"));
        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "F1Score"));
        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "Precision"));
        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "Recall"));
    }

    [Fact]
    public void Apply_WorstClassifier_ReturnsZeroMetrics()
    {
        var data = TestHelper.BinaryData(
            labels: [true, true, false, false],
            scores: [0.1, 0.2, 0.9, 0.8]);

        var transform = new BinaryClassificationMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "Accuracy"));
        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "Precision"));
        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "Recall"));
        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "F1Score"));
    }

    [Fact]
    public void Apply_MixedPredictions_ComputesCorrectAccuracy()
    {
        var data = TestHelper.BinaryData(
            labels: [true, false, true, false, true, false],
            scores: [0.9, 0.1, 0.4, 0.8, 0.7, 0.3]);

        var transform = new BinaryClassificationMetricsTransform();
        var metrics = transform.Apply(data);

        // tp=2(0.9,0.7), tn=2(0.1,0.3), fp=1(0.8), fn=1(0.4)
        Assert.Equal(4.0 / 6.0, TestHelper.ReadMetric(metrics, "Accuracy"), 1e-10);
    }

    [Fact]
    public void Apply_AUC_SeparableData_ReturnsOne()
    {
        var data = TestHelper.BinaryData(
            labels: [true, true, true, false, false, false],
            scores: [0.9, 0.8, 0.7, 0.3, 0.2, 0.1]);

        var transform = new BinaryClassificationMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "AUC"), 1e-10);
    }

    [Fact]
    public void Apply_AUC_TiedScores_ReturnsHalf()
    {
        var data = TestHelper.BinaryData(
            labels: [true, false, true, false],
            scores: [0.5, 0.5, 0.5, 0.5]);

        var transform = new BinaryClassificationMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(0.5, TestHelper.ReadMetric(metrics, "AUC"), 1e-10);
    }

    [Fact]
    public void Apply_LogLoss_ConfidentCorrectPredictions()
    {
        var data = TestHelper.BinaryData(
            labels: [true, false],
            scores: [0.999, 0.001]);

        var transform = new BinaryClassificationMetricsTransform();
        var metrics = transform.Apply(data);

        double logLoss = TestHelper.ReadMetric(metrics, "LogLoss");
        Assert.True(logLoss < 0.01, $"LogLoss should be near 0, got {logLoss}");
        Assert.True(TestHelper.ReadMetric(metrics, "LogLossReduction") > 0.9);
    }

    [Fact]
    public void Apply_CustomThreshold_AffectsResults()
    {
        var data = TestHelper.BinaryData(
            labels: [true, true, false],
            scores: [0.6, 0.4, 0.3]);

        var high = new BinaryClassificationMetricsTransform(threshold: 0.5);
        var low = new BinaryClassificationMetricsTransform(threshold: 0.35);

        var metricsHigh = high.Apply(data);
        var metricsLow = low.Apply(data);

        // With threshold 0.5: tp=1, fn=1, tn=1 → recall=0.5
        // With threshold 0.35: tp=2, fn=0, tn=1 → recall=1.0
        Assert.Equal(0.5, TestHelper.ReadMetric(metricsHigh, "Recall"), 1e-10);
        Assert.Equal(1.0, TestHelper.ReadMetric(metricsLow, "Recall"), 1e-10);
    }

    [Fact]
    public void Apply_AllPositiveLabels_HandlesEdgeCase()
    {
        var data = TestHelper.BinaryData(
            labels: [true, true, true],
            scores: [0.9, 0.8, 0.7]);

        var transform = new BinaryClassificationMetricsTransform();
        var metrics = transform.Apply(data);

        // AUC=0.5 when no negatives
        Assert.Equal(0.5, TestHelper.ReadMetric(metrics, "AUC"), 1e-10);
        Assert.Equal(0.0, TestHelper.ReadMetric(metrics, "NegativePrecision"), 1e-10);
    }

    [Fact]
    public void GetOutputSchema_HasAllMetricColumns()
    {
        var transform = new BinaryClassificationMetricsTransform();
        var schema = transform.GetOutputSchema(
            new HPD.ML.Core.SchemaBuilder().AddColumn<float>("dummy").Build());

        string[] expected = ["AUC", "Accuracy", "F1Score", "Precision", "Recall",
            "LogLoss", "LogLossReduction", "PositivePrecision", "PositiveRecall",
            "NegativePrecision", "NegativeRecall"];

        foreach (var name in expected)
            Assert.NotNull(schema.FindByName(name));
    }

    [Fact]
    public void Apply_FloatLabels_CoercedCorrectly()
    {
        // Use float 1.0/0.0 instead of bool
        var data = TestHelper.Data(
            ("Label", new float[] { 1.0f, 0.0f, 1.0f }),
            ("Score", new double[] { 0.9, 0.1, 0.8 }));

        var transform = new BinaryClassificationMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "Accuracy"), 1e-10);
    }
}
