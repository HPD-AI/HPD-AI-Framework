namespace HPD.ML.Evaluation.Tests;

public class AucAlgorithmTests
{
    [Fact]
    public void ComputeAuc_AllTiesAtHalf_ReturnsHalf()
    {
        double auc = BinaryClassificationMetricsTransform.ComputeAuc(
            [true, false], [0.5, 0.5]);
        Assert.Equal(0.5, auc, 1e-10);
    }

    [Fact]
    public void ComputeAuc_NoPositives_ReturnsHalf()
    {
        double auc = BinaryClassificationMetricsTransform.ComputeAuc(
            [false, false], [0.5, 0.5]);
        Assert.Equal(0.5, auc, 1e-10);
    }

    [Fact]
    public void ComputeAuc_GradualSeparation()
    {
        // T(0.8), T(0.4), F(0.6), F(0.2) → sorted: T(0.8), F(0.6), T(0.4), F(0.2)
        // AUC = 0.75
        double auc = BinaryClassificationMetricsTransform.ComputeAuc(
            [true, true, false, false],
            [0.8, 0.4, 0.6, 0.2]);

        Assert.Equal(0.75, auc, 1e-10);
    }
}
