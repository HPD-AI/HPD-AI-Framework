namespace HPD.ML.Evaluation.Tests;

using HPD.ML.Abstractions;

public class EvaluationExtensionsTests
{
    [Fact]
    public void BinaryClassificationMetrics_ReturnsBinaryTransform()
    {
        ITransform t = ITransform.BinaryClassificationMetrics();
        Assert.IsType<BinaryClassificationMetricsTransform>(t);
    }

    [Fact]
    public void MulticlassMetrics_ReturnsMulticlassTransform()
    {
        ITransform t = ITransform.MulticlassMetrics();
        Assert.IsType<MulticlassMetricsTransform>(t);
    }

    [Fact]
    public void RegressionMetrics_ReturnsRegressionTransform()
    {
        ITransform t = ITransform.RegressionMetrics();
        Assert.IsType<RegressionMetricsTransform>(t);
    }

    [Fact]
    public void RankingMetrics_ReturnsRankingTransform()
    {
        ITransform t = ITransform.RankingMetrics();
        Assert.IsType<RankingMetricsTransform>(t);
    }

    [Fact]
    public void ClusteringMetrics_ReturnsClusteringTransform()
    {
        ITransform t = ITransform.ClusteringMetrics();
        Assert.IsType<ClusteringMetricsTransform>(t);
    }

    [Fact]
    public void ConfusionMatrix_ReturnsConfusionMatrixTransform()
    {
        ITransform t = ITransform.ConfusionMatrix();
        Assert.IsType<ConfusionMatrixTransform>(t);
    }
}
