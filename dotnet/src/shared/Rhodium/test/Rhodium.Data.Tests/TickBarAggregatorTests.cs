using Rhodium.Data.Aggregators;
using Rhodium.Primitives;

namespace Rhodium.Data.Tests;

/// <summary>
/// Tests for TickBarAggregator fixed tick count aggregation.
/// </summary>
public class TickBarAggregatorTests
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
    public void Constructor_ThrowsOnZeroTickCount()
    {
        Assert.Throws<ArgumentException>(() => new TickBarAggregator(0));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeTickCount()
    {
        Assert.Throws<ArgumentException>(() => new TickBarAggregator(-1));
    }

    [Fact]
    public void Constructor_AcceptsPositiveTickCount()
    {
        var aggregator = new TickBarAggregator(10);
        Assert.NotNull(aggregator);
    }

    [Fact]
    public void TryAggregate_FirstTrade_ReturnsFalse()
    {
        var aggregator = new TickBarAggregator(5);
        var trade = CreateTrade(100m, 10m, 0);

        var hasBar = aggregator.TryAggregate(trade, out var bar);

        Assert.False(hasBar);
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_TickCountUnderThreshold_ReturnsFalse()
    {
        var aggregator = new TickBarAggregator(5);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar = aggregator.TryAggregate(t2, out var bar);

        Assert.False(hasBar);
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_TickCountExactlyAtThreshold_ReturnsTrue()
    {
        var aggregator = new TickBarAggregator(3);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 8m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        var hasBar = aggregator.TryAggregate(t3, out var bar);

        Assert.True(hasBar);
        Assert.NotEqual(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_CompletedBar_HasCorrectOHLC()
    {
        var aggregator = new TickBarAggregator(4);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(98m, 8m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(102m, 12m, Duration.FromSeconds(3).Nanos);

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
        var aggregator = new TickBarAggregator(3);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 8m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        var hasBar = aggregator.TryAggregate(t3, out var bar);

        Assert.True(hasBar);
        Assert.Equal(23m, bar.Volume.Value); // 10 + 5 + 8
    }

    [Fact]
    public void TryAggregate_MultipleBars_EachCorrect()
    {
        var aggregator = new TickBarAggregator(2);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 8m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(103m, 12m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar1 = aggregator.TryAggregate(t2, out var bar1);
        aggregator.TryAggregate(t3, out _);
        var hasBar2 = aggregator.TryAggregate(t4, out var bar2);

        Assert.True(hasBar1);
        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(101m, bar1.Close.Value);
        Assert.Equal(15m, bar1.Volume.Value);

        Assert.True(hasBar2);
        Assert.Equal(102m, bar2.Open.Value);
        Assert.Equal(103m, bar2.Close.Value);
        Assert.Equal(20m, bar2.Volume.Value);
    }

    [Fact]
    public void TryAggregate_TickCount_IsTrackedCorrectly()
    {
        var aggregator = new TickBarAggregator(5);
        bool hasBar;

        for (int i = 0; i < 4; i++)
        {
            var trade = CreateTrade(100m + i, 10m, Duration.FromSeconds(i).Nanos);
            hasBar = aggregator.TryAggregate(trade, out _);
            Assert.False(hasBar); // Should not complete yet
        }

        var finalTrade = CreateTrade(104m, 10m, Duration.FromSeconds(4).Nanos);
        hasBar = aggregator.TryAggregate(finalTrade, out var bar);

        Assert.True(hasBar);
    }

    [Fact]
    public void TryAggregate_PeriodIsZero()
    {
        var aggregator = new TickBarAggregator(2);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out var bar);

        Assert.Equal(Duration.Zero, bar.Period);
    }

    [Fact]
    public void Flush_WithPartialData_ReturnsBar()
    {
        var aggregator = new TickBarAggregator(5);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out _);
        var bar = aggregator.Flush();

        Assert.NotNull(bar);
        Assert.Equal(100m, bar.Value.Open.Value);
        Assert.Equal(101m, bar.Value.Close.Value);
        Assert.Equal(15m, bar.Value.Volume.Value);
    }

    [Fact]
    public void Flush_WithoutTrades_ReturnsNull()
    {
        var aggregator = new TickBarAggregator(5);

        var bar = aggregator.Flush();

        Assert.Null(bar);
    }

    [Fact]
    public void Flush_ClearsState()
    {
        var aggregator = new TickBarAggregator(5);
        var trade = CreateTrade(100m, 10m, 0);

        aggregator.TryAggregate(trade, out _);
        aggregator.Flush();

        var bar = aggregator.Flush();
        Assert.Null(bar);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var aggregator = new TickBarAggregator(5);
        var trade = CreateTrade(100m, 10m, 0);

        aggregator.TryAggregate(trade, out _);
        aggregator.Reset();

        var bar = aggregator.Flush();
        Assert.Null(bar);
    }

    [Fact]
    public void Reset_AllowsReinitialization()
    {
        var aggregator = new TickBarAggregator(3);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 8m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        aggregator.TryAggregate(t3, out _);
        aggregator.Reset();

        var trade = CreateTrade(200m, 10m, Duration.FromSeconds(3).Nanos);
        var hasBar = aggregator.TryAggregate(trade, out var bar);

        Assert.False(hasBar); // First trade after reset
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void SmallTickCount_CreatesFrequentBars()
    {
        var aggregator = new TickBarAggregator(2);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);
        var trade3 = CreateTrade(102m, 8m, Duration.FromSeconds(2).Nanos);
        var trade4 = CreateTrade(103m, 7m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar1 = aggregator.TryAggregate(trade2, out var bar1);
        aggregator.TryAggregate(trade3, out _);
        var hasBar2 = aggregator.TryAggregate(trade4, out var bar2);

        Assert.True(hasBar1);
        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(101m, bar1.Close.Value);

        Assert.True(hasBar2);
        Assert.Equal(102m, bar2.Open.Value);
        Assert.Equal(103m, bar2.Close.Value);
    }

    [Fact]
    public void LargeTickCount_RequiresMoreTrades()
    {
        var aggregator = new TickBarAggregator(100);
        bool hasBar = false;

        for (int i = 0; i < 99; i++)
        {
            var trade = CreateTrade(100m, 10m, Duration.FromSeconds(i).Nanos);
            hasBar = aggregator.TryAggregate(trade, out _);
            Assert.False(hasBar); // Should not complete yet
        }

        var finalTrade = CreateTrade(100m, 10m, Duration.FromSeconds(99).Nanos);
        hasBar = aggregator.TryAggregate(finalTrade, out var bar);

        Assert.True(hasBar);
        Assert.Equal(1000m, bar.Volume.Value); // 100 trades * 10
    }

    [Fact]
    public void ConsecutiveBars_ResetCountCorrectly()
    {
        var aggregator = new TickBarAggregator(3);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 8m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(103m, 12m, Duration.FromSeconds(3).Nanos);
        var t5 = CreateTrade(104m, 7m, Duration.FromSeconds(4).Nanos);
        var t6 = CreateTrade(105m, 9m, Duration.FromSeconds(5).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        aggregator.TryAggregate(t3, out var bar1);
        aggregator.TryAggregate(t4, out _);
        aggregator.TryAggregate(t5, out _);
        aggregator.TryAggregate(t6, out var bar2);

        Assert.Equal(23m, bar1.Volume.Value); // 10 + 5 + 8
        Assert.Equal(28m, bar2.Volume.Value); // 12 + 7 + 9
    }

    [Fact]
    public void SingleTradeBars_WorkCorrectly()
    {
        var aggregator = new TickBarAggregator(2);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(100m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(105m, 8m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(105m, 7m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar1 = aggregator.TryAggregate(t2, out var bar1);
        aggregator.TryAggregate(t3, out _);
        var hasBar2 = aggregator.TryAggregate(t4, out var bar2);

        Assert.True(hasBar1);
        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(100m, bar1.High.Value);
        Assert.Equal(100m, bar1.Low.Value);
        Assert.Equal(100m, bar1.Close.Value);
        Assert.Equal(15m, bar1.Volume.Value); // 10 + 5

        Assert.True(hasBar2);
        Assert.Equal(105m, bar2.Open.Value);
        Assert.Equal(105m, bar2.High.Value);
        Assert.Equal(105m, bar2.Low.Value);
        Assert.Equal(105m, bar2.Close.Value);
        Assert.Equal(15m, bar2.Volume.Value); // 8 + 7
    }

    [Fact]
    public void TimeDoesNotAffect_TickAggregation()
    {
        var aggregator = new TickBarAggregator(3);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromDays(1).Nanos); // 1 day later

        aggregator.TryAggregate(t1, out _);
        var hasBar = aggregator.TryAggregate(t2, out _);

        Assert.False(hasBar); // Still under threshold, time doesn't matter
    }

    [Fact]
    public void PriceVariation_DoesNotAffect_TickCount()
    {
        var aggregator = new TickBarAggregator(3);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(200m, 5m, Duration.FromSeconds(1).Nanos); // Large price change
        var t3 = CreateTrade(50m, 8m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        var hasBar = aggregator.TryAggregate(t3, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);
        Assert.Equal(200m, bar.High.Value);
        Assert.Equal(50m, bar.Low.Value);
        Assert.Equal(50m, bar.Close.Value);
    }

    [Fact]
    public void VolumeVariation_DoesNotAffect_TickCount()
    {
        var aggregator = new TickBarAggregator(3);
        var t1 = CreateTrade(100m, 1m, 0);
        var t2 = CreateTrade(101m, 1000m, Duration.FromSeconds(1).Nanos); // Large volume
        var t3 = CreateTrade(102m, 0.1m, Duration.FromSeconds(2).Nanos); // Small volume

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _);
        var hasBar = aggregator.TryAggregate(t3, out var bar);

        Assert.True(hasBar);
        Assert.Equal(1001.1m, bar.Volume.Value); // 1 + 1000 + 0.1
    }

    [Fact]
    public void CountResets_AfterBarCompletion()
    {
        var aggregator = new TickBarAggregator(2);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(102m, 8m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out var bar1); // Completes first bar
        var hasBar2 = aggregator.TryAggregate(t3, out _);

        Assert.True(bar1.Open.Value == 100m);
        Assert.False(hasBar2); // Count reset, need one more trade
    }

    [Fact]
    public void FlushAfterComplete_ReturnsNull()
    {
        var aggregator = new TickBarAggregator(2);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out _); // Completes bar

        var bar = aggregator.Flush();
        Assert.Null(bar); // No partial data after completion
    }

    [Fact]
    public void ManySmallTrades_AggregateCorrectly()
    {
        var aggregator = new TickBarAggregator(10);
        decimal expectedVolume = 0;

        for (int i = 0; i < 9; i++)
        {
            var trade = CreateTrade(100m + i * 0.1m, 1.5m, Duration.FromSeconds(i).Nanos);
            expectedVolume += 1.5m;
            aggregator.TryAggregate(trade, out _);
        }

        var finalTrade = CreateTrade(100.9m, 1.5m, Duration.FromSeconds(9).Nanos);
        expectedVolume += 1.5m;
        var hasBar = aggregator.TryAggregate(finalTrade, out var bar);

        Assert.True(hasBar);
        Assert.Equal(expectedVolume, bar.Volume.Value);
    }

    [Fact]
    public void Currency_IsPreserved()
    {
        var aggregator = new TickBarAggregator(2);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(101m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out var bar);

        Assert.Equal(Currency.USD, bar.Open.Currency);
        Assert.Equal(Currency.USD, bar.High.Currency);
        Assert.Equal(Currency.USD, bar.Low.Currency);
        Assert.Equal(Currency.USD, bar.Close.Currency);
    }
}
