namespace HPD.ML.Evaluation.Tests;

public class ClusteringMetricsTransformTests
{
    [Fact]
    public void Apply_PerfectClustering_NMI_One()
    {
        var data = TestHelper.Data(
            ("Label", new int[] { 0, 0, 1, 1 }),
            ("PredictedLabel", new int[] { 0, 0, 1, 1 }),
            ("Score", new float[][] { [0f], [0f], [0f], [0f] }),
            ("Features", new float[][] { [0f, 0f], [1f, 0f], [10f, 0f], [11f, 0f] }));

        var transform = new ClusteringMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "NormalizedMutualInformation"), 1e-10);
    }

    [Fact]
    public void Apply_RandomClustering_LowNMI()
    {
        var data = TestHelper.Data(
            ("Label", new int[] { 0, 0, 1, 1 }),
            ("PredictedLabel", new int[] { 0, 1, 0, 1 }),
            ("Score", new float[][] { [0f], [0f], [0f], [0f] }),
            ("Features", new float[][] { [0f, 0f], [1f, 0f], [10f, 0f], [11f, 0f] }));

        var transform = new ClusteringMetricsTransform();
        var metrics = transform.Apply(data);

        Assert.True(TestHelper.ReadMetric(metrics, "NormalizedMutualInformation") < 1.0);
    }

    [Fact]
    public void Apply_WithScoreArray_ComputesAvgDistance()
    {
        var data = TestHelper.Data(
            ("Label", new int[] { 0, 1 }),
            ("PredictedLabel", new int[] { 0, 1 }),
            ("Score", new float[][] { [1.0f, 5.0f], [2.0f, 3.0f] }),
            ("Features", new float[][] { [0f, 0f], [10f, 0f] }));

        var transform = new ClusteringMetricsTransform();
        var metrics = transform.Apply(data);

        // Min distances: 1.0 and 2.0 → avg = 1.5
        Assert.Equal(1.5, TestHelper.ReadMetric(metrics, "AverageDistance"), 1e-10);
    }

    [Fact]
    public void Apply_WithFeatures_ComputesDBI()
    {
        // Two tight, well-separated clusters
        var data = TestHelper.Data(
            ("Label", new int[] { 0, 0, 0, 1, 1, 1 }),
            ("PredictedLabel", new int[] { 0, 0, 0, 1, 1, 1 }),
            ("Score", new float[][] { [0f], [0f], [0f], [0f], [0f], [0f] }),
            ("Features", new float[][] {
                [0f, 0f], [0.1f, 0f], [-0.1f, 0f],
                [100f, 0f], [100.1f, 0f], [99.9f, 0f] }));

        var transform = new ClusteringMetricsTransform();
        var metrics = transform.Apply(data);

        double dbi = TestHelper.ReadMetric(metrics, "DaviesBouldinIndex");
        Assert.True(dbi > 0, "DBI should be > 0");
        Assert.True(double.IsFinite(dbi));
    }

    [Fact]
    public void Apply_DBI_WellSeparated_LowValue()
    {
        // Very tight clusters, very far apart → DBI should be small
        var data = TestHelper.Data(
            ("Label", new int[] { 0, 0, 1, 1 }),
            ("PredictedLabel", new int[] { 0, 0, 1, 1 }),
            ("Score", new float[][] { [0f], [0f], [0f], [0f] }),
            ("Features", new float[][] {
                [0f, 0f], [0.001f, 0f],
                [1000f, 0f], [1000.001f, 0f] }));

        var transform = new ClusteringMetricsTransform();
        var metrics = transform.Apply(data);

        double dbi = TestHelper.ReadMetric(metrics, "DaviesBouldinIndex");
        Assert.True(dbi < 0.01, $"DBI should be very small for well-separated clusters, got {dbi}");
    }

    [Fact]
    public void ComputeNmi_IdenticalPartitions_ReturnsOne()
    {
        double nmi = ClusteringMetricsTransform.ComputeNmi([0, 0, 1, 1], [0, 0, 1, 1]);
        Assert.Equal(1.0, nmi, 1e-10);
    }

    [Fact]
    public void ComputeNmi_IndependentPartitions_ReturnsLow()
    {
        double nmi = ClusteringMetricsTransform.ComputeNmi(
            [0, 0, 1, 1], [0, 1, 0, 1]);
        Assert.True(nmi <= 0.5, $"NMI should be low for independent partitions, got {nmi}");
    }

    [Fact]
    public void GetOutputSchema_HasAllColumns()
    {
        var transform = new ClusteringMetricsTransform();
        var schema = transform.GetOutputSchema(
            new HPD.ML.Core.SchemaBuilder().AddColumn<float>("dummy").Build());

        foreach (var name in new[] { "NormalizedMutualInformation", "AverageDistance", "DaviesBouldinIndex" })
            Assert.NotNull(schema.FindByName(name));
    }
}
