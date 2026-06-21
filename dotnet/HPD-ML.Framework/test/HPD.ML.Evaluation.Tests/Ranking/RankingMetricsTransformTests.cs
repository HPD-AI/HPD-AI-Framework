namespace HPD.ML.Evaluation.Tests;

public class RankingMetricsTransformTests
{
    [Fact]
    public void Apply_PerfectRanking_NDCG_One()
    {
        var data = TestHelper.RankingData(
            groups: ["A", "A", "A", "A"],
            labels: [3.0, 2.0, 1.0, 0.0],
            scores: [1.0, 0.8, 0.5, 0.1]);

        var transform = new RankingMetricsTransform(truncationLevels: [3]);
        var metrics = transform.Apply(data);

        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "NDCG@3"), 1e-10);
    }

    [Fact]
    public void Apply_ReversedRanking_LowNDCG()
    {
        var data = TestHelper.RankingData(
            groups: ["A", "A", "A", "A"],
            labels: [3.0, 2.0, 1.0, 0.0],
            scores: [0.1, 0.5, 0.8, 1.0]); // reversed

        var transform = new RankingMetricsTransform(truncationLevels: [3]);
        var metrics = transform.Apply(data);

        Assert.True(TestHelper.ReadMetric(metrics, "NDCG@3") < 1.0);
    }

    [Fact]
    public void Apply_MultipleGroups_AveragesAcross()
    {
        var data = TestHelper.RankingData(
            groups: ["A", "A", "B", "B"],
            labels: [1.0, 0.0, 1.0, 0.0],
            scores: [0.9, 0.1, 0.1, 0.9]); // A perfect, B reversed

        var transform = new RankingMetricsTransform(truncationLevels: [1]);
        var metrics = transform.Apply(data);

        double ndcg = TestHelper.ReadMetric(metrics, "NDCG@1");
        // Group A: NDCG@1=1.0, Group B: NDCG@1=0.0 → average=0.5
        Assert.Equal(0.5, ndcg, 1e-10);
    }

    [Fact]
    public void Apply_CustomTruncationLevels()
    {
        var data = TestHelper.RankingData(
            groups: ["A", "A"],
            labels: [1.0, 0.0],
            scores: [0.9, 0.1]);

        var transform = new RankingMetricsTransform(truncationLevels: [2, 4]);
        var metrics = transform.Apply(data);

        Assert.NotNull(metrics.Schema.FindByName("NDCG@2"));
        Assert.NotNull(metrics.Schema.FindByName("DCG@2"));
        Assert.NotNull(metrics.Schema.FindByName("NDCG@4"));
        Assert.NotNull(metrics.Schema.FindByName("DCG@4"));
        Assert.Null(metrics.Schema.FindByName("NDCG@1"));
    }

    [Fact]
    public void Apply_DefaultTruncationLevels()
    {
        var data = TestHelper.RankingData(
            groups: ["A", "A"],
            labels: [1.0, 0.0],
            scores: [0.9, 0.1]);

        var transform = new RankingMetricsTransform();
        var metrics = transform.Apply(data);

        foreach (var k in new[] { 1, 3, 5, 10 })
        {
            Assert.NotNull(metrics.Schema.FindByName($"NDCG@{k}"));
            Assert.NotNull(metrics.Schema.FindByName($"DCG@{k}"));
        }
    }

    [Fact]
    public void ComputeDcg_KnownValues()
    {
        // DCG([3,2,1]) = (2^3-1)/log2(2) + (2^2-1)/log2(3) + (2^1-1)/log2(4)
        double expected = 7.0 / 1.0 + 3.0 / Math.Log2(3) + 1.0 / Math.Log2(4);
        double actual = RankingMetricsTransform.ComputeDcg([3.0, 2.0, 1.0]);
        Assert.Equal(expected, actual, 1e-10);
    }

    [Fact]
    public void Apply_SingleItemGroup_NDCG_One()
    {
        var data = TestHelper.RankingData(
            groups: ["A"],
            labels: [2.0],
            scores: [0.5]);

        var transform = new RankingMetricsTransform(truncationLevels: [1]);
        var metrics = transform.Apply(data);

        Assert.Equal(1.0, TestHelper.ReadMetric(metrics, "NDCG@1"), 1e-10);
    }
}
