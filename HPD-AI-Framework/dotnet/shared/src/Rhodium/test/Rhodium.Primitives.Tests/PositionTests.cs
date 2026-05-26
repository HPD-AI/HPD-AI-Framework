using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class PositionTests
{
    private static readonly Instrument TestInst = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
    private static readonly InstrumentContract TestContract = Contracts.Equity("AAPL", Venue.NASDAQ, Currency.USD);

    [Fact]
    public void Position_Empty_ShouldCreateFlatPosition()
    {
        // Act
        var position = Position.Empty(TestInst);

        // Assert
        Assert.Equal(TestInst, position.Instrument);
        Assert.Equal(Qty.Zero, position.Quantity);
        Assert.Equal(Price.Zero, position.AvgEntryPrice);
        Assert.Equal(Money.Zero(Currency.USD), position.RealizedPnL);
        Assert.True(position.IsFlat);
    }

    [Fact]
    public void Position_IsFlat_ShouldReturnTrueWhenQuantityZero()
    {
        // Arrange
        var position = Position.Empty(TestInst);

        // Assert
        Assert.True(position.IsFlat);
        Assert.False(position.IsLong);
        Assert.False(position.IsShort);
    }

    [Fact]
    public void Position_IsLong_ShouldReturnTrueWhenQuantityPositive()
    {
        // Arrange
        var position = Position.Empty(TestInst);

        // Act
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(1m));

        // Assert
        Assert.False(position.IsFlat);
        Assert.True(position.IsLong);
        Assert.False(position.IsShort);
        Assert.Equal(Side.Buy, position.Side);
    }

    [Fact]
    public void Position_IsShort_ShouldReturnTrueWhenQuantityNegative()
    {
        // Arrange
        var position = Position.Empty(TestInst);

        // Act
        position.ApplyFill(TestContract, Side.Sell, new Qty(100m), new Price(150m), Money.USD(1m));

        // Assert
        Assert.False(position.IsFlat);
        Assert.False(position.IsLong);
        Assert.True(position.IsShort);
        Assert.Equal(Side.Sell, position.Side);
    }

    [Fact]
    public void Position_ApplyFill_Buy_ShouldIncreaseQuantity()
    {
        // Arrange
        var position = Position.Empty(TestInst);

        // Act
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(1m));

        // Assert
        Assert.Equal(new Qty(100m), position.Quantity);
        Assert.Equal(new Price(150m), position.AvgEntryPrice);
    }

    [Fact]
    public void Position_ApplyFill_Sell_ShouldDecreaseQuantity()
    {
        // Arrange
        var position = Position.Empty(TestInst);

        // Act
        position.ApplyFill(TestContract, Side.Sell, new Qty(100m), new Price(150m), Money.USD(1m));

        // Assert
        Assert.Equal(new Qty(-100m), position.Quantity);
        Assert.Equal(new Price(150m), position.AvgEntryPrice);
    }

    [Fact]
    public void Position_ApplyFill_AddingToPosition_ShouldUpdateAvgPrice()
    {
        // Arrange
        var position = Position.Empty(TestInst);

        // Act - Buy 100 @ 150
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(1m));
        // Buy 100 @ 152
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(152m), Money.USD(1m));

        // Assert - Avg should be 151
        Assert.Equal(new Qty(200m), position.Quantity);
        Assert.Equal(new Price(151m), position.AvgEntryPrice);
    }

    [Fact]
    public void Position_ApplyFill_ReducingPosition_ShouldRealizeProfit()
    {
        // Arrange
        var position = Position.Empty(TestInst);
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(1m));

        // Act - Sell 50 @ 155 (profit of $5 per share)
        position.ApplyFill(TestContract, Side.Sell, new Qty(50m), new Price(155m), Money.USD(0.5m));

        // Assert
        // Realized PnL = (155 - 150) * 50 - commissions = 250 - 1.5 = 248.5
        Assert.Equal(new Qty(50m), position.Quantity);
        Assert.Equal(new Price(150m), position.AvgEntryPrice); // Avg price doesn't change when reducing
        Assert.Equal(248.5m, position.RealizedPnL.Amount);
    }

    [Fact]
    public void Position_ApplyFill_ReducingPosition_ShouldRealizeLoss()
    {
        // Arrange
        var position = Position.Empty(TestInst);
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(1m));

        // Act - Sell 50 @ 145 (loss of $5 per share)
        position.ApplyFill(TestContract, Side.Sell, new Qty(50m), new Price(145m), Money.USD(0.5m));

        // Assert
        // Realized PnL = (145 - 150) * 50 - commissions = -250 - 1.5 = -251.5
        Assert.Equal(new Qty(50m), position.Quantity);
        Assert.Equal(-251.5m, position.RealizedPnL.Amount);
    }

    [Fact]
    public void Position_ApplyFill_ClosingPosition_ShouldSetClosedAt()
    {
        // Arrange
        var position = Position.Empty(TestInst);
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(1m));

        // Act - Close position
        position.ApplyFill(TestContract, Side.Sell, new Qty(100m), new Price(155m), Money.USD(1m));

        // Assert
        Assert.True(position.IsFlat);
        Assert.NotNull(position.ClosedAt);
    }

    [Fact]
    public void Position_ApplyFill_ReversingPosition_ShouldRealizeAndReverse()
    {
        // Arrange
        var position = Position.Empty(TestInst);
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(1m));

        // Act - Sell 150 @ 155 (closes 100 long, opens 50 short)
        position.ApplyFill(TestContract, Side.Sell, new Qty(150m), new Price(155m), Money.USD(1.5m));

        // Assert
        Assert.Equal(new Qty(-50m), position.Quantity);
        Assert.True(position.IsShort);
        // Realized from closing: (155 - 150) * 100 = 500
        // Commission: -2.5 total
        // Then opened 50 short @ 155
        Assert.Equal(497.5m, position.RealizedPnL.Amount);
    }

    [Fact]
    public void Position_Commission_ShouldReduceRealizedPnL()
    {
        // Arrange
        var position = Position.Empty(TestInst);

        // Act - Buy with commission
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(5m));

        // Assert - Commission should be subtracted
        Assert.Equal(-5m, position.RealizedPnL.Amount);
    }

    [Fact]
    public void Position_Value_ShouldUseInstrumentContract()
    {
        var position = Position.Empty(TestInst);
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m, Currency.USD), Money.USD(1m));
        var contract = TestContract;

        var value = position.Value(contract, new Price(155m, Currency.USD));

        Assert.Equal(15500m, value.Notional.Amount);
        Assert.Equal(15500m, value.MarketValue.Amount);
        Assert.Equal(500m, value.UnrealizedPnL.Amount);
        Assert.Equal(-1m, value.RealizedPnL.Amount);
    }

    [Fact]
    public void Position_MultipleRoundTrips_ShouldAccumulateRealizedPnL()
    {
        // Arrange
        var position = Position.Empty(TestInst);

        // Act - Round trip 1: Buy 100 @ 150, Sell 100 @ 155
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(150m), Money.USD(1m));
        position.ApplyFill(TestContract, Side.Sell, new Qty(100m), new Price(155m), Money.USD(1m));

        // Round trip 2: Buy 100 @ 160, Sell 100 @ 165
        position.ApplyFill(TestContract, Side.Buy, new Qty(100m), new Price(160m), Money.USD(1m));
        position.ApplyFill(TestContract, Side.Sell, new Qty(100m), new Price(165m), Money.USD(1m));

        // Assert
        // Trip 1: (155 - 150) * 100 - 2 = 498
        // Trip 2: (165 - 160) * 100 - 2 = 498
        // Total: 996
        Assert.Equal(996m, position.RealizedPnL.Amount);
        Assert.True(position.IsFlat);
    }
}
