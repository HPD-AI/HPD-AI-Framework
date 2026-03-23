namespace HPD.ML.TimeSeries.Tests;

public class AnomalyScorerTests
{
    [Fact]
    public void ComputeRawScore_SignedDifference()
    {
        var score = AnomalyScorer.ComputeRawScore(5, 3, ErrorFunction.SignedDifference);
        Assert.Equal(2.0, score);
    }

    [Fact]
    public void ComputeRawScore_AbsoluteDifference()
    {
        var score = AnomalyScorer.ComputeRawScore(3, 5, ErrorFunction.AbsoluteDifference);
        Assert.Equal(2.0, score);
    }

    [Fact]
    public void ComputeRawScore_SquaredDifference()
    {
        var score = AnomalyScorer.ComputeRawScore(5, 3, ErrorFunction.SquaredDifference);
        Assert.Equal(4.0, score);
    }

    [Fact]
    public void ComputeRawScore_SignedProportion_ZeroPredicted()
    {
        var score = AnomalyScorer.ComputeRawScore(5, 0, ErrorFunction.SignedProportion);
        Assert.Equal(0.0, score);
    }

    [Fact]
    public void ComputeKdePValue_InsufficientHistory_ReturnsHalf()
    {
        var history = new SlidingWindow<double>(100);
        history.Push(1.0);
        var pValue = AnomalyScorer.ComputeKdePValue(5.0, history, AnomalySide.TwoSided);
        Assert.Equal(0.5, pValue);
    }

    [Fact]
    public void ComputeKdePValue_NormalScore_HighPValue()
    {
        var history = new SlidingWindow<double>(100);
        for (int i = 0; i < 50; i++) history.Push(i * 0.1);
        double median = 2.5;
        var pValue = AnomalyScorer.ComputeKdePValue(median, history, AnomalySide.TwoSided);
        Assert.True(pValue > 0.05, $"P-value {pValue} should be high for a normal score");
    }

    [Fact]
    public void ComputeKdePValue_ExtremeScore_LowPValue()
    {
        var history = new SlidingWindow<double>(100);
        for (int i = 0; i < 50; i++) history.Push(i * 0.1);
        var pValue = AnomalyScorer.ComputeKdePValue(100.0, history, AnomalySide.TwoSided);
        Assert.True(pValue < 0.1, $"P-value {pValue} should be low for extreme score");
    }

    [Fact]
    public void UpdateMartingale_Power_AccumulatesEvidence()
    {
        double logM = 0;
        for (int i = 0; i < 10; i++)
            logM = AnomalyScorer.UpdateMartingale(logM, 0.01, MartingaleType.Power);
        Assert.True(logM > 0, $"Log martingale {logM} should accumulate with low p-values");
    }

    [Fact]
    public void ShouldAlert_RawScore_ExceedsThreshold()
    {
        Assert.True(AnomalyScorer.ShouldAlert(10, 0.5, 0, AlertingMode.RawScore, 5));
        Assert.False(AnomalyScorer.ShouldAlert(3, 0.5, 0, AlertingMode.RawScore, 5));
    }

    [Fact]
    public void ShouldAlert_PValue_BelowThreshold()
    {
        Assert.True(AnomalyScorer.ShouldAlert(0, 0.001, 0, AlertingMode.PValueScore, 0.01));
        Assert.False(AnomalyScorer.ShouldAlert(0, 0.5, 0, AlertingMode.PValueScore, 0.01));
    }
}
