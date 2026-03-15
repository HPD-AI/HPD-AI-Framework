using Rhodium.Data.Aggregators;
using Rhodium.Primitives;

namespace Rhodium.Data.Tests;

/// <summary>
/// Tests for BarAggregator time-based aggregation.
/// </summary>
public class BarAggregatorTests
{
    private static Trade CreateTrade(decimal price, decimal size, long nanos)
    {
        return new Trade(
            new Price(price, Currency.USD),
            new Qty(size),
            Side.Buy,
            new DualTimestamp(new Instant(nanos), new Instant(nanos))
        );
    }

    [Fact]
    public void Constructor_ThrowsOnZeroPeriod()
    {
        Assert.Throws<ArgumentException>(() => new BarAggregator(Duration.Zero));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativePeriod()
    {
        Assert.Throws<ArgumentException>(() => new BarAggregator(Duration.FromMinutes(-1)));
    }

    [Fact]
    public void TryAggregate_FirstTrade_ReturnsFalse()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));
        var trade = CreateTrade(100m, 10m, Duration.FromMinutes(0).Nanos);

        var hasBar = aggregator.TryAggregate(trade, out var bar);

        Assert.False(hasBar);
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_TradeWithinPeriod_ReturnsFalse()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(101m, 5m, Duration.FromSeconds(30).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.False(hasBar);
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_TradeAfterPeriod_ReturnsTrue()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(105m, 5m, Duration.FromMinutes(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.NotEqual(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_CompletedBar_HasCorrectOHLC()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(105m, 5m, Duration.FromSeconds(15).Nanos);
        var t3 = CreateTrade(98m, 8m, Duration.FromSeconds(30).Nanos);
        var t4 = CreateTrade(102m, 12m, Duration.FromSeconds(45).Nanos);
        var t5 = CreateTrade(110m, 3m, Duration.FromMinutes(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        aggregator.TryAggregate(t3, out _);
        aggregator.TryAggregate(t4, out _);
        var hasBar = aggregator.TryAggregate(t5, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);  // First trade
        Assert.Equal(105m, bar.High.Value);  // Max price
        Assert.Equal(98m, bar.Low.Value);    // Min price
        Assert.Equal(102m, bar.Close.Value); // Last trade in period
    }

    [Fact]
    public void TryAggregate_CompletedBar_HasCorrectVolume()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(30).Nanos);
        var t3 = CreateTrade(102m, 8m, Duration.FromMinutes(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        var hasBar = aggregator.TryAggregate(t3, out var bar);

        Assert.True(hasBar);
        Assert.Equal(15m, bar.Volume.Value); // 10 + 5
    }

    [Fact]
    public void TryAggregate_MultipleBars_EachCorrect()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));

        // First bar
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromMinutes(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar1 = aggregator.TryAggregate(t2, out var bar1);

        Assert.True(hasBar1);
        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(100m, bar1.Close.Value);

        // Second bar
        var t3 = CreateTrade(102m, 8m, Duration.FromMinutes(2).Nanos);
        var hasBar2 = aggregator.TryAggregate(t3, out var bar2);

        Assert.True(hasBar2);
        Assert.Equal(101m, bar2.Open.Value);
        Assert.Equal(101m, bar2.Close.Value);
    }

    [Fact]
    public void Flush_WithPartialBar_ReturnsBar()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));
        var trade = CreateTrade(100m, 10m, 0);

        aggregator.TryAggregate(trade, out _);
        var bar = aggregator.Flush();

        Assert.NotNull(bar);
        Assert.Equal(100m, bar.Value.Open.Value);
        Assert.Equal(100m, bar.Value.Close.Value);
    }

    [Fact]
    public void Flush_WithoutTrades_ReturnsNull()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));

        var bar = aggregator.Flush();

        Assert.Null(bar);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var aggregator = new BarAggregator(Duration.FromMinutes(1));
        var trade = CreateTrade(100m, 10m, 0);

        aggregator.TryAggregate(trade, out _);
        aggregator.Reset();

        var bar = aggregator.Flush();
        Assert.Null(bar);
    }

    [Fact]
    public void FactoryMethod_Minutes_CreatesCorrectPeriod()
    {
        var aggregator = BarAggregator.Minutes(5);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromMinutes(5).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar = aggregator.TryAggregate(t2, out _);

        Assert.True(hasBar);
    }

    [Fact]
    public void FactoryMethod_Hours_CreatesCorrectPeriod()
    {
        var aggregator = BarAggregator.Hours(1);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromHours(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar = aggregator.TryAggregate(t2, out _);

        Assert.True(hasBar);
    }

    [Fact]
    public void FactoryMethod_Daily_CreatesCorrectPeriod()
    {
        var aggregator = BarAggregator.Daily();
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromDays(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar = aggregator.TryAggregate(t2, out _);

        Assert.True(hasBar);
    }

    [Fact]
    public void AlignToGrid_AlignsTimestamps()
    {
        var aggregator = BarAggregator.Minutes(1);

        // Trade at 00:00:30 should align to 00:00:00
        var t1 = CreateTrade(100m, 10m, Duration.FromSeconds(30).Nanos);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(90).Nanos); // 00:01:30 -> triggers bar

        aggregator.TryAggregate(t1, out _);
        var hasBar = aggregator.TryAggregate(t2, out var bar);

        Assert.True(hasBar);
    }

    [Fact]
    public void SingleTradeBars_WorkCorrectly()
    {
        var aggregator = BarAggregator.Minutes(1);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(105m, 5m, Duration.FromMinutes(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar = aggregator.TryAggregate(t2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);
        Assert.Equal(100m, bar.High.Value);
        Assert.Equal(100m, bar.Low.Value);
        Assert.Equal(100m, bar.Close.Value);
        Assert.Equal(10m, bar.Volume.Value);
    }

    [Fact]
    public void LargeVolumes_AccumulateCorrectly()
    {
        var aggregator = BarAggregator.Minutes(1);

        for (int i = 0; i < 100; i++)
        {
            var trade = CreateTrade(100m, 1000m, Duration.FromSeconds(i / 2).Nanos);
            aggregator.TryAggregate(trade, out _);
        }

        var finalTrade = CreateTrade(100m, 1m, Duration.FromMinutes(1).Nanos);
        var hasBar = aggregator.TryAggregate(finalTrade, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100000m, bar.Volume.Value);
    }
}
