using Rhodium.Data.Aggregators;
using Rhodium.Primitives;

namespace Rhodium.Data.Tests;

/// <summary>
/// Tests for VolumeBarAggregator volume-based aggregation.
/// </summary>
public class VolumeBarAggregatorTests
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
    public void Constructor_ThrowsOnZeroVolume()
    {
        Assert.Throws<ArgumentException>(() => new VolumeBarAggregator(new Qty(0)));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeVolume()
    {
        Assert.Throws<ArgumentException>(() => new VolumeBarAggregator(new Qty(-1)));
    }

    [Fact]
    public void Constructor_AcceptsPositiveVolume()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        Assert.NotNull(aggregator);
    }

    [Fact]
    public void TryAggregate_FirstTrade_ReturnsFalse()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade = CreateTrade(100m, 10m, 0);

        var hasBar = aggregator.TryAggregate(trade, out var bar);

        Assert.False(hasBar);
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_VolumeUnderThreshold_ReturnsFalse()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade1 = CreateTrade(100m, 30m, 0);
        var trade2 = CreateTrade(101m, 30m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.False(hasBar);
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_VolumeExactlyAtThreshold_ReturnsTrue()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade1 = CreateTrade(100m, 50m, 0);
        var trade2 = CreateTrade(101m, 50m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.NotEqual(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_VolumeOverThreshold_ReturnsTrue()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade1 = CreateTrade(100m, 50m, 0);
        var trade2 = CreateTrade(101m, 60m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.NotEqual(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_CompletedBar_HasCorrectOHLC()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var t1 = CreateTrade(100m, 30m, 0);
        var t2 = CreateTrade(105m, 20m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(98m, 30m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(102m, 20m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        aggregator.TryAggregate(t3, out _);
        var hasBar = aggregator.TryAggregate(t4, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);  // First trade
        Assert.Equal(105m, bar.High.Value);  // Max price
        Assert.Equal(98m, bar.Low.Value);    // Min price
        Assert.Equal(102m, bar.Close.Value); // Last trade
    }

    [Fact]
    public void TryAggregate_CompletedBar_HasCorrectVolume()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var t1 = CreateTrade(100m, 30m, 0);
        var t2 = CreateTrade(101m, 40m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 30m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        var hasBar = aggregator.TryAggregate(t3, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Volume.Value); // 30 + 40 + 30
    }

    [Fact]
    public void TryAggregate_MultipleBars_EachCorrect()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var t1 = CreateTrade(100m, 50m, 0);
        var t2 = CreateTrade(101m, 50m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 60m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(103m, 40m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar1 = aggregator.TryAggregate(t2, out var bar1);
        aggregator.TryAggregate(t3, out _);
        var hasBar2 = aggregator.TryAggregate(t4, out var bar2);

        Assert.True(hasBar1);
        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(101m, bar1.Close.Value);
        Assert.Equal(100m, bar1.Volume.Value);

        Assert.True(hasBar2);
        Assert.Equal(102m, bar2.Open.Value);
        Assert.Equal(103m, bar2.Close.Value);
        Assert.Equal(100m, bar2.Volume.Value);
    }

    [Fact]
    public void TryAggregate_SingleTradeExceedsThreshold_ReturnsTrue()
    {
        var aggregator = new VolumeBarAggregator(new Qty(50));
        var trade1 = CreateTrade(100m, 20m, 0);
        var trade2 = CreateTrade(101m, 100m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(120m, bar.Volume.Value);
    }

    [Fact]
    public void TryAggregate_VolumeAccumulation_IsCorrect()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 20m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 30m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(103m, 40m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);  // 10
        aggregator.TryAggregate(t2, out _);  // 30
        aggregator.TryAggregate(t3, out _);  // 60
        var hasBar = aggregator.TryAggregate(t4, out var bar); // 100

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Volume.Value);
    }

    [Fact]
    public void TryAggregate_PeriodIsZero()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade1 = CreateTrade(100m, 50m, 0);
        var trade2 = CreateTrade(101m, 50m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out var bar);

        Assert.Equal(Duration.Zero, bar.Period);
    }

    [Fact]
    public void Flush_WithPartialData_ReturnsBar()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade = CreateTrade(100m, 30m, 0);

        aggregator.TryAggregate(trade, out _);
        var bar = aggregator.Flush();

        Assert.NotNull(bar);
        Assert.Equal(100m, bar.Value.Open.Value);
        Assert.Equal(100m, bar.Value.Close.Value);
        Assert.Equal(30m, bar.Value.Volume.Value);
    }

    [Fact]
    public void Flush_WithoutTrades_ReturnsNull()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));

        var bar = aggregator.Flush();

        Assert.Null(bar);
    }

    [Fact]
    public void Flush_ClearsState()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade = CreateTrade(100m, 30m, 0);

        aggregator.TryAggregate(trade, out _);
        aggregator.Flush();

        var bar = aggregator.Flush();
        Assert.Null(bar);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade = CreateTrade(100m, 30m, 0);

        aggregator.TryAggregate(trade, out _);
        aggregator.Reset();

        var bar = aggregator.Flush();
        Assert.Null(bar);
    }

    [Fact]
    public void Reset_AllowsReinitialization()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade1 = CreateTrade(100m, 50m, 0);
        var trade2 = CreateTrade(101m, 50m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out _);
        aggregator.Reset();

        var trade3 = CreateTrade(200m, 30m, Duration.FromSeconds(2).Nanos);
        var hasBar = aggregator.TryAggregate(trade3, out var bar);

        Assert.False(hasBar); // First trade after reset
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void SmallVolumeThreshold_CreatesFrequentBars()
    {
        var aggregator = new VolumeBarAggregator(new Qty(10));
        var trade1 = CreateTrade(100m, 5m, 0);
        var trade2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(10m, bar.Volume.Value);
    }

    [Fact]
    public void LargeVolumeThreshold_RequiresMoreTrades()
    {
        var aggregator = new VolumeBarAggregator(new Qty(1000));
        var hasBar = false;

        for (int i = 0; i < 9; i++)
        {
            var trade = CreateTrade(100m, 100m, Duration.FromSeconds(i).Nanos);
            hasBar = aggregator.TryAggregate(trade, out _);
            Assert.False(hasBar); // Should not complete yet
        }

        var finalTrade = CreateTrade(100m, 100m, Duration.FromSeconds(10).Nanos);
        hasBar = aggregator.TryAggregate(finalTrade, out var bar);

        Assert.True(hasBar);
        Assert.Equal(1000m, bar.Volume.Value);
    }

    [Fact]
    public void VaryingTradeSizes_AccumulateCorrectly()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var t1 = CreateTrade(100m, 1m, 0);
        var t2 = CreateTrade(101m, 10m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 50m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(103m, 39m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        aggregator.TryAggregate(t3, out _);
        var hasBar = aggregator.TryAggregate(t4, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Volume.Value); // 1 + 10 + 50 + 39
    }

    [Fact]
    public void TimeDoesNotAffect_VolumeAggregation()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade1 = CreateTrade(100m, 50m, 0);
        var trade2 = CreateTrade(101m, 49m, Duration.FromDays(1).Nanos); // 1 day later

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out _);

        Assert.False(hasBar); // Still under threshold, time doesn't matter
    }

    [Fact]
    public void DecimalVolumes_AccumulateCorrectly()
    {
        var aggregator = new VolumeBarAggregator(new Qty(10.5m));
        var t1 = CreateTrade(100m, 5.25m, 0);
        var t2 = CreateTrade(101m, 5.25m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar = aggregator.TryAggregate(t2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(10.5m, bar.Volume.Value);
    }

    [Fact]
    public void ConsecutiveBars_ResetVolumeCorrectly()
    {
        var aggregator = new VolumeBarAggregator(new Qty(50));
        var t1 = CreateTrade(100m, 30m, 0);
        var t2 = CreateTrade(101m, 20m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 30m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(103m, 20m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out var bar1); // Volume: 50
        aggregator.TryAggregate(t3, out _);
        aggregator.TryAggregate(t4, out var bar2); // Volume: 50

        Assert.Equal(50m, bar1.Volume.Value);
        Assert.Equal(50m, bar2.Volume.Value);
    }

    [Fact]
    public void SingleTradeBars_WorkCorrectly()
    {
        var aggregator = new VolumeBarAggregator(new Qty(50));
        var trade1 = CreateTrade(100m, 25m, 0);
        var trade2 = CreateTrade(100m, 25m, Duration.FromSeconds(1).Nanos);
        var trade3 = CreateTrade(105m, 25m, Duration.FromSeconds(2).Nanos);
        var trade4 = CreateTrade(105m, 25m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar1 = aggregator.TryAggregate(trade2, out var bar1);
        aggregator.TryAggregate(trade3, out _);
        var hasBar2 = aggregator.TryAggregate(trade4, out var bar2);

        Assert.True(hasBar1);
        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(100m, bar1.High.Value);
        Assert.Equal(100m, bar1.Low.Value);
        Assert.Equal(100m, bar1.Close.Value);
        Assert.Equal(50m, bar1.Volume.Value);

        Assert.True(hasBar2);
        Assert.Equal(105m, bar2.Open.Value);
        Assert.Equal(105m, bar2.High.Value);
        Assert.Equal(105m, bar2.Low.Value);
        Assert.Equal(105m, bar2.Close.Value);
        Assert.Equal(50m, bar2.Volume.Value);
    }

    [Fact]
    public void Currency_IsPreserved()
    {
        var aggregator = new VolumeBarAggregator(new Qty(100));
        var trade1 = CreateTrade(100m, 50m, 0);
        var trade2 = CreateTrade(101m, 50m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out var bar);

        Assert.Equal(Currency.USD, bar.Open.Currency);
        Assert.Equal(Currency.USD, bar.High.Currency);
        Assert.Equal(Currency.USD, bar.Low.Currency);
        Assert.Equal(Currency.USD, bar.Close.Currency);
    }
}
