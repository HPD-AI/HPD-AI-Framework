using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class QuoteTests
{
    [Fact]
    public void Quote_ShouldStoreBidAskAndSizes()
    {
        // Arrange
        var bid = new Price(100m, Currency.USD);
        var ask = new Price(101m, Currency.USD);
        var bidSize = new Qty(500m);
        var askSize = new Qty(300m);
        var time = DualTimestamp.Synchronized(Instant.Now);

        // Act
        var quote = new Quote(bid, ask, bidSize, askSize, time);

        // Assert
        Assert.Equal(bid, quote.Bid);
        Assert.Equal(ask, quote.Ask);
        Assert.Equal(bidSize, quote.BidSize);
        Assert.Equal(askSize, quote.AskSize);
        Assert.Equal(time, quote.Time);
    }

    [Fact]
    public void Quote_Mid_ShouldCalculateCorrectly()
    {
        // Arrange
        var quote = new Quote(
            new Price(100m, Currency.USD),
            new Price(102m, Currency.USD),
            new Qty(500m),
            new Qty(300m),
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var mid = quote.Mid;

        // Assert
        Assert.Equal(101m, mid.Value);
    }

    [Fact]
    public void Quote_Spread_ShouldCalculateCorrectly()
    {
        // Arrange
        var quote = new Quote(
            new Price(100m, Currency.USD),
            new Price(101m, Currency.USD),
            new Qty(500m),
            new Qty(300m),
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var spread = quote.Spread;

        // Assert
        Assert.Equal(1m, spread.Value);
    }

    [Fact]
    public void Quote_SpreadBps_ShouldCalculateCorrectly()
    {
        // Arrange
        var quote = new Quote(
            new Price(100m, Currency.USD),
            new Price(101m, Currency.USD),
            new Qty(500m),
            new Qty(300m),
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var spreadBps = quote.SpreadBps;

        // Assert - (101-100) / 100.5 * 10000 = 99.5
        Assert.True(spreadBps > 99m && spreadBps < 100m);
    }

    [Fact]
    public void Quote_Staleness_ShouldReturnFeedLatency()
    {
        // Arrange
        var exchTime = Instant.FromUnixMillis(1000);
        var localTime = Instant.FromUnixMillis(1050);
        var time = new DualTimestamp(exchTime, localTime);
        var quote = new Quote(
            new Price(100m),
            new Price(101m),
            new Qty(500m),
            new Qty(300m),
            time
        );

        // Act
        var staleness = quote.Staleness;

        // Assert
        Assert.Equal(Duration.FromMillis(50), staleness);
    }

    [Fact]
    public void Quote_BidTick_ShouldConvertToTickPrice()
    {
        // Arrange
        var quote = new Quote(
            new Price(100.50m),
            new Price(101m),
            new Qty(500m),
            new Qty(300m),
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var bidTick = quote.BidTick(0.01m);

        // Assert
        Assert.Equal(10050, bidTick.Ticks);
    }

    [Fact]
    public void Quote_Empty_ShouldCreateZeroQuote()
    {
        // Arrange
        var time = DualTimestamp.Synchronized(Instant.Now);

        // Act
        var empty = Quote.Empty(time);

        // Assert
        Assert.Equal(Price.Zero, empty.Bid);
        Assert.Equal(Price.Zero, empty.Ask);
        Assert.Equal(Qty.Zero, empty.BidSize);
        Assert.Equal(Qty.Zero, empty.AskSize);
    }
}

public class TradeTests
{
    [Fact]
    public void Trade_ShouldStorePriceSizeAndSide()
    {
        // Arrange
        var price = new Price(100.50m, Currency.USD);
        var size = new Qty(250m);
        var side = Side.Buy;
        var time = DualTimestamp.Synchronized(Instant.Now);

        // Act
        var trade = new Trade(price, size, side, time);

        // Assert
        Assert.Equal(price, trade.Price);
        Assert.Equal(size, trade.Size);
        Assert.Equal(side, trade.AggressorSide);
        Assert.Equal(time, trade.Time);
    }

    [Fact]
    public void Trade_PriceTick_ShouldConvertToTickPrice()
    {
        // Arrange
        var trade = new Trade(
            new Price(100.50m),
            new Qty(250m),
            Side.Buy,
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var priceTick = trade.PriceTick(0.01m);

        // Assert
        Assert.Equal(10050, priceTick.Ticks);
    }
}

public class DepthUpdateTests
{
    [Fact]
    public void DepthUpdate_ShouldStoreSidePriceAndSize()
    {
        // Arrange
        var side = Side.Buy;
        var priceTick = new TickPrice(10050, 0.01m);
        var size = new Qty(1000m);
        var time = DualTimestamp.Synchronized(Instant.Now);

        // Act
        var update = new DepthUpdate(side, priceTick, size, time);

        // Assert
        Assert.Equal(side, update.Side);
        Assert.Equal(priceTick, update.PriceTick);
        Assert.Equal(size, update.Size);
        Assert.Equal(time, update.Time);
    }
}

public class TickTests
{
    [Fact]
    public void Tick_ShouldStorePriceSizeAndType()
    {
        // Arrange
        var price = new Price(100.50m);
        var size = new Qty(100m);
        var type = TickType.Trade;
        var time = DualTimestamp.Synchronized(Instant.Now);

        // Act
        var tick = new Tick(price, size, type, time);

        // Assert
        Assert.Equal(price, tick.Price);
        Assert.Equal(size, tick.Size);
        Assert.Equal(type, tick.Type);
        Assert.Equal(time, tick.Time);
    }

    [Fact]
    public void Tick_PriceTick_ShouldConvertToTickPrice()
    {
        // Arrange
        var tick = new Tick(
            new Price(100.50m),
            new Qty(100m),
            TickType.Bid,
            DualTimestamp.Synchronized(Instant.Now)
        );

        // Act
        var priceTick = tick.PriceTick(0.01m);

        // Assert
        Assert.Equal(10050, priceTick.Ticks);
    }
}

public class TickTypeTests
{
    [Fact]
    public void TickType_ShouldHaveCorrectValues()
    {
        // Assert
        Assert.Equal((byte)0, (byte)TickType.Trade);
        Assert.Equal((byte)1, (byte)TickType.Bid);
        Assert.Equal((byte)2, (byte)TickType.Ask);
        Assert.Equal((byte)3, (byte)TickType.BidDepth);
        Assert.Equal((byte)4, (byte)TickType.AskDepth);
    }
}
