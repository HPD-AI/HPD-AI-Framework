namespace HPD.ML.TimeSeries.Tests;

public class StlDecompositionTests
{
    [Fact]
    public void Decompose_SumEqualsOriginal()
    {
        var series = new double[48];
        for (int i = 0; i < 48; i++)
            series[i] = Math.Sin(2 * Math.PI * i / 12) + 0.1 * i;

        var (seasonal, trend, residual) = StlDecomposition.Decompose(series, period: 12);

        for (int i = 0; i < series.Length; i++)
        {
            double sum = seasonal[i] + trend[i] + residual[i];
            Assert.True(Math.Abs(sum - series[i]) < 1e-6,
                $"Index {i}: S+T+R={sum} != original={series[i]}");
        }
    }

    [Fact]
    public void Decompose_PureTrend_SeasonalNearZero()
    {
        var series = new double[48];
        for (int i = 0; i < 48; i++)
            series[i] = 2.0 * i + 10;

        var (seasonal, trend, _) = StlDecomposition.Decompose(series, period: 4);

        double seasonalRms = Math.Sqrt(seasonal.Average(x => x * x));
        double trendRms = Math.Sqrt(trend.Average(x => x * x));
        Assert.True(seasonalRms < trendRms * 0.1,
            $"Seasonal RMS {seasonalRms} should be << trend RMS {trendRms}");
    }

    [Fact]
    public void Decompose_PureSeasonal_TrendNearConstant()
    {
        var pattern = new double[] { 1, 2, 3, 4 };
        var series = new double[40];
        for (int i = 0; i < 40; i++)
            series[i] = pattern[i % 4];

        var (_, trend, _) = StlDecomposition.Decompose(series, period: 4);

        double trendMean = trend.Average();
        double trendVariation = trend.Max() - trend.Min();
        Assert.True(trendVariation < 2.0,
            $"Trend variation {trendVariation} should be small for pure seasonal");
    }

    [Fact]
    public void Decompose_ResidualSmallForCleanData()
    {
        var series = new double[48];
        for (int i = 0; i < 48; i++)
            series[i] = Math.Sin(2 * Math.PI * i / 12) + 0.5 * i;

        var (_, _, residual) = StlDecomposition.Decompose(series, period: 12, iterations: 3);

        double residualRms = Math.Sqrt(residual.Average(x => x * x));
        double seriesRms = Math.Sqrt(series.Average(x => x * x));
        Assert.True(residualRms < seriesRms,
            $"Residual RMS {residualRms} should be < series RMS {seriesRms}");
    }

    [Fact]
    public void Decompose_ShortSeries_Throws()
    {
        var series = new double[5];
        Assert.Throws<ArgumentException>(() =>
            StlDecomposition.Decompose(series, period: 4));
    }

    [Fact]
    public void Decompose_MultipleIterations_ConvergesMore()
    {
        var series = new double[48];
        for (int i = 0; i < 48; i++)
            series[i] = Math.Sin(2 * Math.PI * i / 12) + 0.5 * i;

        var (_, _, residual1) = StlDecomposition.Decompose(series, period: 12, iterations: 1);
        var (_, _, residual3) = StlDecomposition.Decompose(series, period: 12, iterations: 3);

        double rms1 = Math.Sqrt(residual1.Average(x => x * x));
        double rms3 = Math.Sqrt(residual3.Average(x => x * x));
        Assert.True(rms3 <= rms1 + 0.1,
            $"Residual RMS with 3 iter ({rms3}) should be <= 1 iter ({rms1})");
    }
}
