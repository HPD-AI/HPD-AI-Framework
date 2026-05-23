using Rhodium.Simulation;
using Rhodium.HFT;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Tests.Models;

/// <summary>
/// Tests for SlippageCalculator slippage calculations.
/// </summary>
public class SlippageCalculatorTests
{
    private class MockHftDepth : IHftDepth
    {
        public decimal TickSize => 1m;
        public decimal LotSize => 1m;
        public long? BestBidTick => null;
        public long? BestAskTick => null;

        public decimal QtyAtTick(Side side, long priceTick) => 0m;
        public int CopyLevels(Side side, Span<global::Rhodium.HFT.DepthLevel> destination) => 0;
        public void Update(Side side, long priceTick, decimal qty, Instant timestamp) { }
        public void Clear(Side side = Side.None) { }
    }

    [Fact]
    public void ApplySlippage_NoneModel_ReturnsOriginalPrice()
    {
        // Arrange
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty = new Qty(50m);
        var slippage = new SlippageParams(SlippageModelType.None);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // Assert
        Assert.Equal(100m, adjustedPrice.Value);
        Assert.Equal(Currency.USD, adjustedPrice.Currency);
    }

    [Fact]
    public void ApplySlippage_VolumeProportional_BuyOrder_IncreasesPrice()
    {
        // Arrange
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty = new Qty(10m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // Assert
        // Slippage = 10 * 1bps = 10bps = 0.1%
        // Amount = 100 * 0.001 = 0.1
        // Buy pays more: 100 + 0.1 = 100.1
        Assert.Equal(100.1m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_VolumeProportional_SellOrder_DecreasesPrice()
    {
        // Arrange
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty = new Qty(10m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Sell, slippage, depth);

        // Assert
        // Slippage = 10 * 1bps = 10bps = 0.1%
        // Amount = 100 * 0.001 = 0.1
        // Sell receives less: 100 - 0.1 = 99.9
        Assert.Equal(99.9m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_VolumeProportional_ScalesWithQuantity()
    {
        // Arrange
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty1 = new Qty(10m);
        var fillQty2 = new Qty(20m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m);
        var depth = new MockHftDepth();

        // Act
        var price1 = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty1, Side.Buy, slippage, depth);
        var price2 = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty2, Side.Buy, slippage, depth);

        // Assert
        // 10 qty: 100 + 0.1 = 100.1
        // 20 qty: 100 + 0.2 = 100.2
        Assert.Equal(100.1m, price1.Value);
        Assert.Equal(100.2m, price2.Value);
        Assert.True(price2.Value > price1.Value);
    }

    [Fact]
    public void ApplySlippage_VolumeProportional_CalculationFormula()
    {
        // Arrange
        var nominalPrice = new Price(1000m, Currency.USD);
        var fillQty = new Qty(50m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 2m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // Assert
        // Slippage = 50 * 2bps = 100bps = 1%
        // Amount = 1000 * 0.01 = 10
        // Buy pays more: 1000 + 10 = 1010
        Assert.Equal(1010m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_VolumeProportional_LargeQuantity()
    {
        // Arrange
        var nominalPrice = new Price(50m, Currency.USD);
        var fillQty = new Qty(1000m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 0.5m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // Assert
        // Slippage = 1000 * 0.5bps = 500bps = 5%
        // Amount = 50 * 0.05 = 2.5
        // Buy pays more: 50 + 2.5 = 52.5
        Assert.Equal(52.5m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_VolumeProportional_WithReferenceQuantityUsesParticipation()
    {
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty = new Qty(10m);
        var slippage = SlippageParams.VolumeProportional(
            bpsPerLotSize: 50m,
            referenceQuantity: 100m);
        var depth = new MockHftDepth();

        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // 10% participation * 50bps = 5bps.
        Assert.Equal(100.05m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_VolatilityAdjusted_AddsVolatilityBps()
    {
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty = new Qty(10m);
        var slippage = SlippageParams.VolatilityAdjusted(
            bpsPerLotSize: 50m,
            volatilityBps: 20m,
            referenceQuantity: 100m);
        var depth = new MockHftDepth();

        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Sell, slippage, depth);

        // 10% participation * 50bps + 20bps volatility = 25bps.
        Assert.Equal(99.75m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_NeverGoesBelowZero_Buy()
    {
        // Arrange
        var nominalPrice = new Price(1m, Currency.USD);
        var fillQty = new Qty(100000m); // Huge quantity
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // Assert - Buy price increases, so always > 0
        Assert.True(adjustedPrice.Value >= 0);
    }

    [Fact]
    public void ApplySlippage_NeverGoesBelowZero_Sell()
    {
        // Arrange
        var nominalPrice = new Price(1m, Currency.USD);
        var fillQty = new Qty(100000m); // Huge quantity
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Sell, slippage, depth);

        // Assert - Capped at 0
        Assert.Equal(0m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_ZeroQuantity_NoSlippage()
    {
        // Arrange
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty = new Qty(0m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 10m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // Assert
        Assert.Equal(100m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_PreservesCurrency()
    {
        // Arrange
        var nominalPrice = new Price(0.05m, Currency.BTC);
        var fillQty = new Qty(10m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // Assert
        Assert.Equal(Currency.BTC, adjustedPrice.Currency);
    }

    [Fact]
    public void ApplySlippage_SmallQuantity_SmallSlippage()
    {
        // Arrange
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty = new Qty(1m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 0.5m);
        var depth = new MockHftDepth();

        // Act
        var adjustedPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);

        // Assert
        // Slippage = 1 * 0.5bps = 0.5bps = 0.005%
        // Amount = 100 * 0.00005 = 0.005
        // Buy pays more: 100 + 0.005 = 100.005
        Assert.Equal(100.005m, adjustedPrice.Value);
    }

    [Fact]
    public void ApplySlippage_BuyAndSell_SymmetricAdverseSlippage()
    {
        // Arrange
        var nominalPrice = new Price(100m, Currency.USD);
        var fillQty = new Qty(10m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m);
        var depth = new MockHftDepth();

        // Act
        var buyPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Buy, slippage, depth);
        var sellPrice = SlippageCalculator.ApplySlippage(
            nominalPrice, fillQty, Side.Sell, slippage, depth);

        // Assert - Both are adverse (buyer pays more, seller receives less)
        Assert.True(buyPrice.Value > nominalPrice.Value);
        Assert.True(sellPrice.Value < nominalPrice.Value);
        Assert.Equal(100.1m, buyPrice.Value);
        Assert.Equal(99.9m, sellPrice.Value);
    }

    [Fact]
    public void Calculate_ReturnsCappedSellAdjustment()
    {
        var nominalPrice = new Price(1m, Currency.USD);
        var fillQty = new Qty(100000m);
        var slippage = new SlippageParams(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m);

        var adjustment = slippage.Calculate(nominalPrice, fillQty, Side.Sell);

        Assert.Equal(Money.USD(-1m), adjustment);
    }
}
