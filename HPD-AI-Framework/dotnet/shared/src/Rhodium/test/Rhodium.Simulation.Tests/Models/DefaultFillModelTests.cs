using Rhodium.Simulation;
using Rhodium.HFT;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Tests.Models;

/// <summary>
/// Tests for DefaultFillModel fill logic.
/// </summary>
public class DefaultFillModelTests
{
    private class MockHftDepth : IHftDepth
    {
        public decimal TickSize => 1m;
        public decimal LotSize => 1m;
        public long? BestBidTick { get; set; }
        public long? BestAskTick { get; set; }

        public decimal QtyAtTick(Side side, long priceTick) => 0m;
        public int CopyLevels(Side side, Span<global::Rhodium.HFT.DepthLevel> destination) => 0;
        public void Update(Side side, long priceTick, decimal qty, Instant timestamp) { }
        public void Clear(Side side = Side.None) { }
    }

    private static FillModelContext CreateContext(
        long orderPriceTick,
        Side orderSide,
        long? bestBid = null,
        long? bestAsk = null,
        double queuePosition = 0.0,
        Qty? orderQty = null)
    {
        return new FillModelContext
        {
            OrderPriceTick = orderPriceTick,
            OrderSide = orderSide,
            BestBidTick = bestBid,
            BestAskTick = bestAsk,
            QueueRelativePosition = queuePosition,
            OrderQty = orderQty ?? new Qty(100m),
            NominalFillPrice = new Price(orderPriceTick, Currency.USD),
            Depth = new MockHftDepth { BestBidTick = bestBid, BestAskTick = bestAsk },
            Trade = null
        };
    }

    [Fact]
    public void ShouldFillLimit_BuyOrderCrossesSpread_ReturnsTrue()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 105,
            orderSide: Side.Buy,
            bestBid: 100,
            bestAsk: 103
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert - buy @ 105 crosses ask @ 103
        Assert.True(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_SellOrderCrossesSpread_ReturnsTrue()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 98,
            orderSide: Side.Sell,
            bestBid: 100,
            bestAsk: 103
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert - sell @ 98 crosses bid @ 100
        Assert.True(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_BuyOrderAtBestBid_FrontOfQueue_ReturnsTrue()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 100,
            orderSide: Side.Buy,
            bestBid: 100,
            bestAsk: 101,
            queuePosition: 0.005 // Front 0.5% (< 1%)
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.True(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_SellOrderAtBestAsk_FrontOfQueue_ReturnsTrue()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 101,
            orderSide: Side.Sell,
            bestBid: 100,
            bestAsk: 101,
            queuePosition: 0.009 // Front 0.9% (< 1%)
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.True(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_BuyOrderNotAtFrontOfQueue_ReturnsFalse()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 100,
            orderSide: Side.Buy,
            bestBid: 100,
            bestAsk: 101,
            queuePosition: 0.5 // 50% back in queue
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.False(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_SellOrderNotAtFrontOfQueue_ReturnsFalse()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 101,
            orderSide: Side.Sell,
            bestBid: 100,
            bestAsk: 101,
            queuePosition: 0.2 // 20% back in queue
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.False(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_BuyOrderNoBestAsk_ReturnsFalse()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 100,
            orderSide: Side.Buy,
            bestBid: 99,
            bestAsk: null,
            queuePosition: 0.5
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.False(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_SellOrderNoBestBid_ReturnsFalse()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 100,
            orderSide: Side.Sell,
            bestBid: null,
            bestAsk: 101,
            queuePosition: 0.5
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.False(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_BuyOrderBelowBestBid_ReturnsFalse()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 98,
            orderSide: Side.Buy,
            bestBid: 100,
            bestAsk: 101,
            queuePosition: 0.005
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.False(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_SellOrderAboveBestAsk_ReturnsFalse()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 103,
            orderSide: Side.Sell,
            bestBid: 100,
            bestAsk: 101,
            queuePosition: 0.005
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.False(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_BuyOrderAtBestBidExactly1Percent_ReturnsFalse()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 100,
            orderSide: Side.Buy,
            bestBid: 100,
            bestAsk: 101,
            queuePosition: 0.01 // Exactly 1% (not < 1%)
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.False(shouldFill);
    }

    [Fact]
    public void ShouldFillLimit_BuyOrderAtBestBidJustUnder1Percent_ReturnsTrue()
    {
        // Arrange
        var model = new DefaultFillModel();
        var ctx = CreateContext(
            orderPriceTick: 100,
            orderSide: Side.Buy,
            bestBid: 100,
            bestAsk: 101,
            queuePosition: 0.00999 // Just under 1%
        );

        // Act
        var shouldFill = model.ShouldFillLimit(ref ctx);

        // Assert
        Assert.True(shouldFill);
    }

    [Fact]
    public void AdjustFillPrice_ReturnsNominalPriceUnchanged()
    {
        // Arrange
        var model = new DefaultFillModel();
        var nominalPrice = new Price(123.45m, Currency.USD);
        var ctx = CreateContext(
            orderPriceTick: 100,
            orderSide: Side.Buy
        );
        ctx = ctx with { NominalFillPrice = nominalPrice };

        // Act
        var adjustedPrice = model.AdjustFillPrice(ref ctx);

        // Assert
        Assert.Equal(nominalPrice.Value, adjustedPrice.Value);
        Assert.Equal(nominalPrice.Currency, adjustedPrice.Currency);
    }

    [Fact]
    public void AdjustFillPrice_WithDifferentCurrency_PreservesOriginal()
    {
        // Arrange
        var model = new DefaultFillModel();
        var nominalPrice = new Price(0.05m, Currency.BTC);
        var ctx = CreateContext(
            orderPriceTick: 5000,
            orderSide: Side.Sell
        );
        ctx = ctx with { NominalFillPrice = nominalPrice };

        // Act
        var adjustedPrice = model.AdjustFillPrice(ref ctx);

        // Assert
        Assert.Equal(0.05m, adjustedPrice.Value);
        Assert.Equal(Currency.BTC, adjustedPrice.Currency);
    }
}
