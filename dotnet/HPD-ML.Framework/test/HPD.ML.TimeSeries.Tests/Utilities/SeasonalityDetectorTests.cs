namespace HPD.ML.TimeSeries.Tests;

public class SeasonalityDetectorTests
{
    [Fact]
    public void DetectPeriod_ClearSinusoid_CorrectPeriod()
    {
        var series = new double[120];
        for (int i = 0; i < 120; i++)
            series[i] = Math.Sin(2 * Math.PI * i / 12);
        int period = SeasonalityDetector.DetectPeriod(series);
        Assert.Equal(12, period);
    }

    [Fact]
    public void DetectPeriod_NoSeasonality_ReturnsNegative()
    {
        var rng = new Random(42);
        var series = new double[200];
        for (int i = 0; i < 200; i++)
            series[i] = rng.NextDouble();
        int period = SeasonalityDetector.DetectPeriod(series);
        Assert.Equal(-1, period);
    }

    [Fact]
    public void DetectPeriod_ShortSeries_ReturnsNegative()
    {
        var series = new double[10];
        for (int i = 0; i < 10; i++) series[i] = i;
        Assert.Equal(-1, SeasonalityDetector.DetectPeriod(series));
    }

    [Fact]
    public void DetectPeriod_ConstantSeries_ReturnsNegative()
    {
        var series = new double[100];
        Array.Fill(series, 5.0);
        Assert.Equal(-1, SeasonalityDetector.DetectPeriod(series));
    }

    [Fact]
    public void DetectPeriod_CompositePeriods_FindsDominant()
    {
        var series = new double[240];
        for (int i = 0; i < 240; i++)
            series[i] = Math.Sin(2 * Math.PI * i / 7) + 0.3 * Math.Sin(2 * Math.PI * i / 30);
        int period = SeasonalityDetector.DetectPeriod(series);
        Assert.Equal(7, period);
    }
}
