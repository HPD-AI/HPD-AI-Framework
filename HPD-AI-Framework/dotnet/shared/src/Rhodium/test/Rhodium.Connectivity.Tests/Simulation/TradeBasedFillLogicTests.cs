using Rhodium.Connectivity.Simulation;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Tests.Simulation;

/// <summary>
/// Tests for TradeBasedFillLogic aggressor-side fill modeling.
/// </summary>
public class TradeBasedFillLogicTests
{
    private static Trade CreateTrade(decimal price, decimal size, Side aggressorSide)
    {
        return new Trade(
            new Price(price, Currency.USD),
            new Qty(size),
            aggressorSide,
            new DualTimestamp(new Instant(0), new Instant(0))
        );
    }

    [Fact]
    public void CanFillFromTrade_BuyOrderWithSellAggressor_ReturnsTrue()
    {
        // Arrange
        long orderPriceTick = 100; // Buy @ 100
        var orderSide = Side.Buy;
        var trade = CreateTrade(100m, 10m, Side.Sell); // Sell aggressor @ 100
        decimal tickSize = 1m;
        long? bestBid = 99;
        long? bestAsk = 101;

        // Act
        var canFill = TradeBasedFillLogic.CanFillFromTrade(
            orderPriceTick, orderSide, trade, tickSize, bestBid, bestAsk);

        // Assert
        Assert.True(canFill);
    }

    [Fact]
    public void CanFillFromTrade_SellOrderWithBuyAggressor_ReturnsTrue()
    {
        // Arrange
        long orderPriceTick = 100; // Sell @ 100
        var orderSide = Side.Sell;
        var trade = CreateTrade(100m, 10m, Side.Buy); // Buy aggressor @ 100
        decimal tickSize = 1m;
        long? bestBid = 99;
        long? bestAsk = 101;

        // Act
        var canFill = TradeBasedFillLogic.CanFillFromTrade(
            orderPriceTick, orderSide, trade, tickSize, bestBid, bestAsk);

        // Assert
        Assert.True(canFill);
    }

    [Fact]
    public void CanFillFromTrade_BuyOrderWithBuyAggressor_ReturnsFalse()
    {
        // Arrange
        long orderPriceTick = 100; // Buy @ 100
        var orderSide = Side.Buy;
        var trade = CreateTrade(100m, 10m, Side.Buy); // Buy aggressor (wrong side)
        decimal tickSize = 1m;

        // Act
        var canFill = TradeBasedFillLogic.CanFillFromTrade(
            orderPriceTick, orderSide, trade, tickSize, null, null);

        // Assert
        Assert.False(canFill);
    }

    [Fact]
    public void CanFillFromTrade_SellOrderWithSellAggressor_ReturnsFalse()
    {
        // Arrange
        long orderPriceTick = 100; // Sell @ 100
        var orderSide = Side.Sell;
        var trade = CreateTrade(100m, 10m, Side.Sell); // Sell aggressor (wrong side)
        decimal tickSize = 1m;

        // Act
        var canFill = TradeBasedFillLogic.CanFillFromTrade(
            orderPriceTick, orderSide, trade, tickSize, null, null);

        // Assert
        Assert.False(canFill);
    }

    [Fact]
    public void CanFillFromTrade_BuyOrderBelowTradePrice_ReturnsFalse()
    {
        // Arrange
        long orderPriceTick = 99; // Buy @ 99
        var orderSide = Side.Buy;
        var trade = CreateTrade(100m, 10m, Side.Sell); // Trade @ 100
        decimal tickSize = 1m;

        // Act
        var canFill = TradeBasedFillLogic.CanFillFromTrade(
            orderPriceTick, orderSide, trade, tickSize, null, null);

        // Assert
        Assert.False(canFill);
    }

    [Fact]
    public void CanFillFromTrade_SellOrderAboveTradePrice_ReturnsFalse()
    {
        // Arrange
        long orderPriceTick = 101; // Sell @ 101
        var orderSide = Side.Sell;
        var trade = CreateTrade(100m, 10m, Side.Buy); // Trade @ 100
        decimal tickSize = 1m;

        // Act
        var canFill = TradeBasedFillLogic.CanFillFromTrade(
            orderPriceTick, orderSide, trade, tickSize, null, null);

        // Assert
        Assert.False(canFill);
    }

    [Fact]
    public void CanFillFromTrade_BuyOrderCrossesEffectiveAsk_ReturnsTrue()
    {
        // Arrange
        long orderPriceTick = 102; // Buy @ 102 (aggressive)
        var orderSide = Side.Buy;
        var trade = CreateTrade(100m, 10m, Side.Sell); // Trade @ 100
        decimal tickSize = 1m;
        long? bestBid = 99;
        long? bestAsk = 103;

        // Act
        var canFill = TradeBasedFillLogic.CanFillFromTrade(
            orderPriceTick, orderSide, trade, tickSize, bestBid, bestAsk);

        // Assert - effective ask is min(103, 100) = 100, order @ 102 crosses
        Assert.True(canFill);
    }

    [Fact]
    public void CanFillFromTrade_SellOrderCrossesEffectiveBid_ReturnsTrue()
    {
        // Arrange
        long orderPriceTick = 98; // Sell @ 98 (aggressive)
        var orderSide = Side.Sell;
        var trade = CreateTrade(100m, 10m, Side.Buy); // Trade @ 100
        decimal tickSize = 1m;
        long? bestBid = 97;
        long? bestAsk = 101;

        // Act
        var canFill = TradeBasedFillLogic.CanFillFromTrade(
            orderPriceTick, orderSide, trade, tickSize, bestBid, bestAsk);

        // Assert - effective bid is max(97, 100) = 100, order @ 98 crosses
        Assert.True(canFill);
    }

    [Fact]
    public void GetTradeDrivenFillPrice_ReturnsOrderLimitPrice()
    {
        // Arrange
        long orderPriceTick = 100;
        decimal tickSize = 0.5m;
        var currency = Currency.USD;

        // Act
        var fillPrice = TradeBasedFillLogic.GetTradeDrivenFillPrice(
            orderPriceTick, tickSize, currency);

        // Assert
        Assert.Equal(50m, fillPrice.Value); // 100 * 0.5
        Assert.Equal(Currency.USD, fillPrice.Currency);
    }

    [Fact]
    public void GetTradeDrivenFillPrice_WithDifferentTickSize_CalculatesCorrectly()
    {
        // Arrange
        long orderPriceTick = 1000;
        decimal tickSize = 0.01m;
        var currency = Currency.BTC;

        // Act
        var fillPrice = TradeBasedFillLogic.GetTradeDrivenFillPrice(
            orderPriceTick, tickSize, currency);

        // Assert
        Assert.Equal(10m, fillPrice.Value); // 1000 * 0.01
        Assert.Equal(Currency.BTC, fillPrice.Currency);
    }

    [Fact]
    public void DetermineFillQuantity_NoPartialFill_ReturnsFullOrderQuantity()
    {
        // Arrange
        var orderRemainingQty = new Qty(100m);
        var trade = CreateTrade(100m, 30m, Side.Buy);
        var behavior = FillBehavior.NoPartialFill;

        // Act
        var fillQty = TradeBasedFillLogic.DetermineFillQuantity(
            orderRemainingQty, trade, behavior);

        // Assert
        Assert.Equal(100m, fillQty.Value);
    }

    [Fact]
    public void DetermineFillQuantity_PartialFillOnTrade_ReturnsTradeSize()
    {
        // Arrange
        var orderRemainingQty = new Qty(100m);
        var trade = CreateTrade(100m, 30m, Side.Buy);
        var behavior = FillBehavior.PartialFillOnTrade;

        // Act
        var fillQty = TradeBasedFillLogic.DetermineFillQuantity(
            orderRemainingQty, trade, behavior);

        // Assert
        Assert.Equal(30m, fillQty.Value);
    }

    [Fact]
    public void DetermineFillQuantity_PartialFillCappedAtTradeSize()
    {
        // Arrange
        var orderRemainingQty = new Qty(50m); // Order smaller than trade
        var trade = CreateTrade(100m, 100m, Side.Buy);
        var behavior = FillBehavior.PartialFillOnTrade;

        // Act
        var fillQty = TradeBasedFillLogic.DetermineFillQuantity(
            orderRemainingQty, trade, behavior);

        // Assert
        Assert.Equal(50m, fillQty.Value);
    }

    [Fact]
    public void DetermineFillQuantity_PartialFillCappedAtRemainingOrderSize()
    {
        // Arrange
        var orderRemainingQty = new Qty(20m); // Order smaller than trade
        var trade = CreateTrade(100m, 50m, Side.Buy);
        var behavior = FillBehavior.PartialFillOnTrade;

        // Act
        var fillQty = TradeBasedFillLogic.DetermineFillQuantity(
            orderRemainingQty, trade, behavior);

        // Assert
        Assert.Equal(20m, fillQty.Value);
    }

    [Fact]
    public void DetermineFillQuantity_NoTrade_ReturnsFullOrderQuantity()
    {
        // Arrange
        var orderRemainingQty = new Qty(100m);
        Trade? trade = null;
        var behavior = FillBehavior.PartialFillOnTrade;

        // Act
        var fillQty = TradeBasedFillLogic.DetermineFillQuantity(
            orderRemainingQty, trade, behavior);

        // Assert
        Assert.Equal(100m, fillQty.Value);
    }

    [Fact]
    public void DetermineFillQuantity_ExactMatch_ReturnsThatQuantity()
    {
        // Arrange
        var orderRemainingQty = new Qty(50m);
        var trade = CreateTrade(100m, 50m, Side.Buy);
        var behavior = FillBehavior.PartialFillOnTrade;

        // Act
        var fillQty = TradeBasedFillLogic.DetermineFillQuantity(
            orderRemainingQty, trade, behavior);

        // Assert
        Assert.Equal(50m, fillQty.Value);
    }
}
