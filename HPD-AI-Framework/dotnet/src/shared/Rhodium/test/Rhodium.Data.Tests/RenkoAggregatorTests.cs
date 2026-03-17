using Rhodium.Data.Aggregators;
using Rhodium.Primitives;

namespace Rhodium.Data.Tests;

/// <summary>
/// Tests for RenkoAggregator price-based brick aggregation.
/// </summary>
public class RenkoAggregatorTests
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
    public void Constructor_ThrowsOnZeroBrickSize()
    {
        Assert.Throws<ArgumentException>(() => new RenkoAggregator(0m));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeBrickSize()
    {
        Assert.Throws<ArgumentException>(() => new RenkoAggregator(-1m));
    }

    [Fact]
    public void Constructor_AcceptsPositiveBrickSize()
    {
        var aggregator = new RenkoAggregator(1m);
        Assert.NotNull(aggregator);
    }

    [Fact]
    public void TryAggregate_FirstTrade_ReturnsFalse()
    {
        var aggregator = new RenkoAggregator(1m);
        var trade = CreateTrade(100m, 10m, 0);

        var hasBar = aggregator.TryAggregate(trade, out var bar);

        Assert.False(hasBar);
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_PriceMoveLessThanBrickSize_ReturnsFalse()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(104m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.False(hasBar);
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_PriceMoveExactlyBrickSize_ReturnsTrue()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.NotEqual(default(Bar), bar);
    }

    [Fact]
    public void TryAggregate_UpBrick_HasCorrectOHLC()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);   // Starting price
        Assert.Equal(105m, bar.High.Value);   // Max of open and close
        Assert.Equal(100m, bar.Low.Value);    // Min of open and close
        Assert.Equal(105m, bar.Close.Value);  // Open + brick size
    }

    [Fact]
    public void TryAggregate_DownBrick_HasCorrectOHLC()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(95m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);   // Starting price
        Assert.Equal(100m, bar.High.Value);   // Max of open and close
        Assert.Equal(95m, bar.Low.Value);     // Min of open and close
        Assert.Equal(95m, bar.Close.Value);   // Open - brick size
    }

    [Fact]
    public void TryAggregate_UpBrick_HasCorrectVolume()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(105m, 7m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(7m, bar.Volume.Value); // Volume of triggering trade
    }

    [Fact]
    public void TryAggregate_MultipleBricks_EachCorrect()
    {
        var aggregator = new RenkoAggregator(5m);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(110m, 8m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(t1, out _);
        var hasBar1 = aggregator.TryAggregate(t2, out var bar1);
        var hasBar2 = aggregator.TryAggregate(t3, out var bar2);

        Assert.True(hasBar1);
        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(105m, bar1.Close.Value);

        Assert.True(hasBar2);
        Assert.Equal(105m, bar2.Open.Value);
        Assert.Equal(110m, bar2.Close.Value);
    }

    [Fact]
    public void TryAggregate_LargePriceMove_CreatesOneBrick()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(120m, 5m, Duration.FromSeconds(1).Nanos); // Move of 20, but only creates one brick

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);
        Assert.Equal(105m, bar.Close.Value); // Only one brick size
    }

    [Fact]
    public void TryAggregate_DirectionChange_CreatesCorrectBrick()
    {
        var aggregator = new RenkoAggregator(5m);
        var t1 = CreateTrade(100m, 10m, 0);
        var t2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(100m, 8m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out var bar1); // Up brick to 105
        var hasBar2 = aggregator.TryAggregate(t3, out var bar2);

        Assert.True(hasBar2);
        Assert.Equal(105m, bar2.Open.Value);
        Assert.Equal(100m, bar2.Close.Value); // Down brick from 105
    }

    [Fact]
    public void TryAggregate_PeriodIsZero()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out var bar);

        Assert.Equal(Duration.Zero, bar.Period);
    }

    [Fact]
    public void Flush_WithPartialData_ReturnsNull()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade = CreateTrade(100m, 10m, 0);

        aggregator.TryAggregate(trade, out _);
        var bar = aggregator.Flush();

        Assert.Null(bar);
    }

    [Fact]
    public void Flush_WithoutTrades_ReturnsNull()
    {
        var aggregator = new RenkoAggregator(5m);

        var bar = aggregator.Flush();

        Assert.Null(bar);
    }

    [Fact]
    public void Flush_AfterCompleteBrick_ReturnsNull()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out _);
        var bar = aggregator.Flush();

        Assert.Null(bar);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade = CreateTrade(100m, 10m, 0);

        aggregator.TryAggregate(trade, out _);
        aggregator.Reset();

        var bar = aggregator.Flush();
        Assert.Null(bar);
    }

    [Fact]
    public void Reset_AllowsReinitialization()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out _);
        aggregator.Reset();

        var trade3 = CreateTrade(200m, 10m, Duration.FromSeconds(2).Nanos);
        var hasBar = aggregator.TryAggregate(trade3, out var bar);

        Assert.False(hasBar); // First trade after reset
        Assert.Equal(default(Bar), bar);
    }

    [Fact]
    public void SmallBrickSize_CreatesFrequentBricks()
    {
        var aggregator = new RenkoAggregator(0.5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(100.5m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);
        Assert.Equal(100.5m, bar.Close.Value);
    }

    [Fact]
    public void LargeBrickSize_RequiresLargerMoves()
    {
        var aggregator = new RenkoAggregator(50m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(149m, 5m, Duration.FromSeconds(1).Nanos);
        var trade3 = CreateTrade(150m, 5m, Duration.FromSeconds(2).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar1 = aggregator.TryAggregate(trade2, out _);
        var hasBar2 = aggregator.TryAggregate(trade3, out var bar);

        Assert.False(hasBar1); // 49 move, not enough
        Assert.True(hasBar2);  // 50 move, creates brick
        Assert.Equal(100m, bar.Open.Value);
        Assert.Equal(150m, bar.Close.Value);
    }

    [Fact]
    public void NegativePriceMove_CreatesDownBrick()
    {
        var aggregator = new RenkoAggregator(10m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(90m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out var bar);

        Assert.True(hasBar);
        Assert.Equal(100m, bar.Open.Value);
        Assert.Equal(90m, bar.Close.Value);
        Assert.Equal(100m, bar.High.Value);
        Assert.Equal(90m, bar.Low.Value);
    }

    [Fact]
    public void ConsecutiveUpBricks_MaintainCorrectState()
    {
        var aggregator = new RenkoAggregator(5m);
        var t1 = CreateTrade(100m, 1m, 0);
        var t2 = CreateTrade(105m, 2m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(110m, 3m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(115m, 4m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out var bar1);
        aggregator.TryAggregate(t3, out var bar2);
        aggregator.TryAggregate(t4, out var bar3);

        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(105m, bar1.Close.Value);
        Assert.Equal(105m, bar2.Open.Value);
        Assert.Equal(110m, bar2.Close.Value);
        Assert.Equal(110m, bar3.Open.Value);
        Assert.Equal(115m, bar3.Close.Value);
    }

    [Fact]
    public void ConsecutiveDownBricks_MaintainCorrectState()
    {
        var aggregator = new RenkoAggregator(5m);
        var t1 = CreateTrade(100m, 1m, 0);
        var t2 = CreateTrade(95m, 2m, Duration.FromSeconds(1).Nanos);
        var t3 = CreateTrade(90m, 3m, Duration.FromSeconds(2).Nanos);
        var t4 = CreateTrade(85m, 4m, Duration.FromSeconds(3).Nanos);

        aggregator.TryAggregate(t1, out _);
        aggregator.TryAggregate(t2, out var bar1);
        aggregator.TryAggregate(t3, out var bar2);
        aggregator.TryAggregate(t4, out var bar3);

        Assert.Equal(100m, bar1.Open.Value);
        Assert.Equal(95m, bar1.Close.Value);
        Assert.Equal(95m, bar2.Open.Value);
        Assert.Equal(90m, bar2.Close.Value);
        Assert.Equal(90m, bar3.Open.Value);
        Assert.Equal(85m, bar3.Close.Value);
    }

    [Fact]
    public void TimeIncreases_ButNoEffect_OnBrickFormation()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(102m, 5m, Duration.FromDays(1).Nanos); // 1 day later, but small price move

        aggregator.TryAggregate(trade1, out _);
        var hasBar = aggregator.TryAggregate(trade2, out _);

        Assert.False(hasBar); // Time doesn't matter, only price
    }

    [Fact]
    public void Currency_IsPreserved()
    {
        var aggregator = new RenkoAggregator(5m);
        var trade1 = CreateTrade(100m, 10m, 0);
        var trade2 = CreateTrade(105m, 5m, Duration.FromSeconds(1).Nanos);

        aggregator.TryAggregate(trade1, out _);
        aggregator.TryAggregate(trade2, out var bar);

        Assert.Equal(Currency.USD, bar.Open.Currency);
        Assert.Equal(Currency.USD, bar.High.Currency);
        Assert.Equal(Currency.USD, bar.Low.Currency);
        Assert.Equal(Currency.USD, bar.Close.Currency);
    }
}
