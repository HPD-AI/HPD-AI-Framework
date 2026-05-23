using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class SubmitOrderTests
{
    private static readonly Instrument TestInst = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void SubmitOrder_Market_ShouldCreateMarketOrder()
    {
        // Act
        var cmd = SubmitOrder.Market(new StrategyId(1), TestInst, Side.Buy, new Qty(100m));

        // Assert
        Assert.Equal(OrderType.Market, cmd.Type);
        Assert.Equal(TestInst, cmd.Instrument);
        Assert.Equal(Side.Buy, cmd.Side);
        Assert.Equal(100m, cmd.Quantity.Value);
        Assert.Null(cmd.LimitPrice);
        Assert.Null(cmd.StopPrice);
        Assert.True(cmd.OrderId.Value > 0);
    }

    [Fact]
    public void SubmitOrder_Limit_ShouldCreateLimitOrder()
    {
        // Arrange
        var limitPrice = new Price(150m, Currency.USD);

        // Act
        var cmd = SubmitOrder.Limit(new StrategyId(1), TestInst, Side.Sell, new Qty(50m), limitPrice);

        // Assert
        Assert.Equal(OrderType.Limit, cmd.Type);
        Assert.Equal(limitPrice, cmd.LimitPrice);
        Assert.Null(cmd.StopPrice);
    }

    [Fact]
    public void SubmitOrder_IcebergLimit_ShouldStoreDisplayQuantity()
    {
        var cmd = SubmitOrder.IcebergLimit(
            new StrategyId(1),
            TestInst,
            Side.Buy,
            new Qty(100m),
            new Price(150m, Currency.USD),
            new Qty(10m));

        Assert.Equal(OrderType.Limit, cmd.Type);
        Assert.Equal(100m, cmd.Quantity.Value);
        Assert.Equal(10m, cmd.DisplayQuantity?.Value);
    }

    [Fact]
    public void SubmitOrder_StopMarket_ShouldCreateStopMarketOrder()
    {
        // Arrange
        var stopPrice = new Price(145m);

        // Act
        var cmd = SubmitOrder.StopMarket(new StrategyId(1), TestInst, Side.Sell, new Qty(100m), stopPrice);

        // Assert
        Assert.Equal(OrderType.StopMarket, cmd.Type);
        Assert.Equal(stopPrice, cmd.StopPrice);
        Assert.Null(cmd.LimitPrice);
    }

    [Fact]
    public void SubmitOrder_StopLimit_ShouldCreateStopLimitOrder()
    {
        // Arrange
        var stopPrice = new Price(145m);
        var limitPrice = new Price(144m);

        // Act
        var cmd = SubmitOrder.StopLimit(new StrategyId(1), TestInst, Side.Sell, new Qty(100m), stopPrice, limitPrice);

        // Assert
        Assert.Equal(OrderType.StopLimit, cmd.Type);
        Assert.Equal(stopPrice, cmd.StopPrice);
        Assert.Equal(limitPrice, cmd.LimitPrice);
    }

    [Fact]
    public void SubmitOrder_Buy_ShouldCreateBuyMarketOrder()
    {
        // Act
        var cmd = SubmitOrder.Buy(new StrategyId(1), TestInst, new Qty(100m));

        // Assert
        Assert.Equal(OrderType.Market, cmd.Type);
        Assert.Equal(Side.Buy, cmd.Side);
    }

    [Fact]
    public void SubmitOrder_Sell_ShouldCreateSellMarketOrder()
    {
        // Act
        var cmd = SubmitOrder.Sell(new StrategyId(1), TestInst, new Qty(100m));

        // Assert
        Assert.Equal(OrderType.Market, cmd.Type);
        Assert.Equal(Side.Sell, cmd.Side);
    }

    [Fact]
    public void SubmitOrder_BuyLimit_ShouldCreateBuyLimitOrder()
    {
        // Arrange
        var limitPrice = new Price(150m);

        // Act
        var cmd = SubmitOrder.BuyLimit(new StrategyId(1), TestInst, new Qty(100m), limitPrice);

        // Assert
        Assert.Equal(OrderType.Limit, cmd.Type);
        Assert.Equal(Side.Buy, cmd.Side);
        Assert.Equal(limitPrice, cmd.LimitPrice);
    }

    [Fact]
    public void SubmitOrder_SellLimit_ShouldCreateSellLimitOrder()
    {
        // Arrange
        var limitPrice = new Price(150m);

        // Act
        var cmd = SubmitOrder.SellLimit(new StrategyId(1), TestInst, new Qty(100m), limitPrice);

        // Assert
        Assert.Equal(OrderType.Limit, cmd.Type);
        Assert.Equal(Side.Sell, cmd.Side);
        Assert.Equal(limitPrice, cmd.LimitPrice);
    }

    [Fact]
    public void SubmitOrder_TrailingStop_ShouldCreateTrailingStopOrder()
    {
        // Act
        var cmd = SubmitOrder.TrailingStop(new StrategyId(1), TestInst, Side.Sell, new Qty(100m), 2.50m);

        // Assert
        Assert.Equal(OrderType.TrailingStopMarket, cmd.Type);
        Assert.Equal(2.50m, cmd.TrailingOffset);
        Assert.Equal((TrailingOffsetType)1, cmd.TrailingOffsetType); // Price = 1
        Assert.Equal(TimeInForce.GTC, cmd.TimeInForce);
    }

    [Fact]
    public void SubmitOrder_TrailingStop_WithPercentOffset_ShouldStorePercent()
    {
        // Act
        var cmd = SubmitOrder.TrailingStop(new StrategyId(1), 
            TestInst,
            Side.Sell,
            new Qty(100m),
            5m,
            (TrailingOffsetType)3); // Percent

        // Assert
        Assert.Equal(5m, cmd.TrailingOffset);
        Assert.Equal((TrailingOffsetType)3, cmd.TrailingOffsetType);
    }

    [Fact]
    public void SubmitOrder_TrailingStopLimit_ShouldCreateTrailingStopLimitOrder()
    {
        // Arrange
        var limitOffset = new Price(1m);

        // Act
        var cmd = SubmitOrder.TrailingStopLimit(new StrategyId(1), 
            TestInst,
            Side.Sell,
            new Qty(100m),
            2.50m,
            (TrailingOffsetType)1, // Price
            limitOffset);

        // Assert
        Assert.Equal(OrderType.TrailingStopLimit, cmd.Type);
        Assert.Equal(2.50m, cmd.TrailingOffset);
        Assert.Equal(limitOffset, cmd.LimitPrice);
    }

    [Fact]
    public void SubmitOrder_MarketIfTouched_ShouldCreateMITOrder()
    {
        // Arrange
        var triggerPrice = new Price(148m);

        // Act
        var cmd = SubmitOrder.MarketIfTouched(new StrategyId(1), TestInst, Side.Buy, new Qty(100m), triggerPrice);

        // Assert
        Assert.Equal(OrderType.MarketIfTouched, cmd.Type);
        Assert.Equal(triggerPrice, cmd.StopPrice);
        Assert.Null(cmd.LimitPrice);
    }

    [Fact]
    public void SubmitOrder_LimitIfTouched_ShouldCreateLITOrder()
    {
        // Arrange
        var triggerPrice = new Price(148m);
        var limitPrice = new Price(147m);

        // Act
        var cmd = SubmitOrder.LimitIfTouched(new StrategyId(1), TestInst, Side.Buy, new Qty(100m), triggerPrice, limitPrice);

        // Assert
        Assert.Equal(OrderType.LimitIfTouched, cmd.Type);
        Assert.Equal(triggerPrice, cmd.StopPrice);
        Assert.Equal(limitPrice, cmd.LimitPrice);
    }

    [Fact]
    public void SubmitOrder_WithAlgorithm_ShouldStoreAlgorithmDetails()
    {
        // Arrange
        var algoParams = new Dictionary<string, string> { ["key"] = "value" };

        // Act
        var cmd = SubmitOrder.WithAlgorithm(new StrategyId(1), TestInst, Side.Buy, new Qty(100m), "CustomAlgo", algoParams);

        // Assert
        Assert.Equal("CustomAlgo", cmd.ExecAlgorithmId);
        Assert.Equal(algoParams, cmd.ExecAlgorithmParams);
    }

    [Fact]
    public void SubmitOrder_Twap_ShouldCreateTwapOrder()
    {
        // Arrange
        var horizon = TimeSpan.FromHours(2);
        var interval = TimeSpan.FromMinutes(5);

        // Act
        var cmd = SubmitOrder.Twap(new StrategyId(1), TestInst, Side.Buy, new Qty(1000m), horizon, interval);

        // Assert
        Assert.Equal("TWAP", cmd.ExecAlgorithmId);
        Assert.NotNull(cmd.ExecAlgorithmParams);
        Assert.Equal("7200", cmd.ExecAlgorithmParams["horizon_secs"]);
        Assert.Equal("300", cmd.ExecAlgorithmParams["interval_secs"]);
    }

    [Fact]
    public void SubmitOrder_Vwap_ShouldCreateVwapOrder()
    {
        // Arrange
        var horizon = TimeSpan.FromHours(1);

        // Act
        var cmd = SubmitOrder.Vwap(new StrategyId(1), TestInst, Side.Sell, new Qty(500m), horizon, 0.2m);

        // Assert
        Assert.Equal("VWAP", cmd.ExecAlgorithmId);
        Assert.NotNull(cmd.ExecAlgorithmParams);
        Assert.Equal("3600", cmd.ExecAlgorithmParams["horizon_secs"]);
        Assert.Equal("0.2", cmd.ExecAlgorithmParams["participation_rate"]);
    }

    [Fact]
    public void SubmitOrder_Pov_ShouldCreateParticipationOrder()
    {
        var cmd = SubmitOrder.Pov(
            new StrategyId(1),
            TestInst,
            Side.Buy,
            new Qty(500m),
            participationRate: 0.25m,
            horizon: TimeSpan.FromMinutes(30));

        Assert.Equal("POV", cmd.ExecAlgorithmId);
        Assert.NotNull(cmd.ExecAlgorithmParams);
        Assert.Equal("0.25", cmd.ExecAlgorithmParams["participation_rate"]);
        Assert.Equal("1800", cmd.ExecAlgorithmParams["horizon_secs"]);
    }

    [Fact]
    public void SubmitOrder_WithVariantId_ShouldStoreVariantId()
    {
        // Act
        var cmd = SubmitOrder.Market(new StrategyId(1), TestInst, Side.Buy, new Qty(100m), variantId: 42);

        // Assert
        Assert.Equal(42, cmd.VariantId);
    }

    [Fact]
    public void SubmitOrder_WithNumericTag_ShouldStoreNumericTag()
    {
        // Act
        var cmd = SubmitOrder.Market(new StrategyId(1), TestInst, Side.Buy, new Qty(100m), numericTag: 12345L);

        // Assert
        Assert.Equal(12345L, cmd.NumericTag);
    }

    [Fact]
    public void SubmitOrder_ShouldImplementICommand()
    {
        // Act
        var cmd = SubmitOrder.Market(new StrategyId(1), TestInst, Side.Buy, new Qty(100m));

        // Assert
        Assert.IsAssignableFrom<ICommand>(cmd);
    }
}

public class ExecutionSpecTests
{
    [Fact]
    public void GoodTil_ShouldSetGtdTimeInForceAndExpiry()
    {
        var expiry = Instant.FromUnixSeconds(1_000);

        var spec = Execution.Limit().At(new Price(100m)).GoodTil(expiry);

        Assert.Equal(TimeInForce.GTD, spec.TimeInForce);
        Assert.Equal(expiry, spec.GoodTilDate);
        Assert.Equal(OrderType.Limit, spec.OrderType);
        Assert.Equal(new Price(100m), spec.LimitPrice);
    }

    [Fact]
    public void ChainedExecutionSpecOptions_ShouldPreserveGoodTilDate()
    {
        var expiry = Instant.FromUnixSeconds(2_000);

        var spec = Execution.Limit()
            .GoodTil(expiry)
            .AtBid()
            .Display(new Qty(25m))
            .WithPostOnly()
            .WithMaxSlippageTicks(3);

        Assert.Equal(TimeInForce.GTD, spec.TimeInForce);
        Assert.Equal(expiry, spec.GoodTilDate);
        Assert.True(spec.PostOnly);
        Assert.Equal(3, spec.MaxSlippageTicks);
        Assert.Equal(25m, spec.DisplayQuantity?.Value);
    }
}

public class CancelOrderTests
{
    [Fact]
    public void CancelOrder_ShouldStoreOrderId()
    {
        // Arrange
        var orderId = OrderId.New();

        // Act
        var cmd = new CancelOrder(orderId);

        // Assert
        Assert.Equal(orderId, cmd.OrderId);
    }

    [Fact]
    public void CancelOrder_ShouldImplementICommand()
    {
        // Act
        var cmd = new CancelOrder(OrderId.New());

        // Assert
        Assert.IsAssignableFrom<ICommand>(cmd);
    }
}

public class CancelAllOrdersTests
{
    private static readonly Instrument TestInst = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void CancelAllOrders_WithNoFilters_ShouldHaveNullFilters()
    {
        // Act
        var cmd = new CancelAllOrders();

        // Assert
        Assert.Null(cmd.Instrument);
        Assert.Null(cmd.Side);
    }

    [Fact]
    public void CancelAllOrders_WithInstrumentFilter_ShouldStoreInstrument()
    {
        // Act
        var cmd = new CancelAllOrders(Instrument: TestInst);

        // Assert
        Assert.Equal(TestInst, cmd.Instrument);
        Assert.Null(cmd.Side);
    }

    [Fact]
    public void CancelAllOrders_WithSideFilter_ShouldStoreSide()
    {
        // Act
        var cmd = new CancelAllOrders(Side: Side.Buy);

        // Assert
        Assert.Null(cmd.Instrument);
        Assert.Equal(Side.Buy, cmd.Side);
    }

    [Fact]
    public void CancelAllOrders_WithBothFilters_ShouldStoreBoth()
    {
        // Act
        var cmd = new CancelAllOrders(Instrument: TestInst, Side: Side.Sell);

        // Assert
        Assert.Equal(TestInst, cmd.Instrument);
        Assert.Equal(Side.Sell, cmd.Side);
    }

    [Fact]
    public void CancelAllOrders_ShouldImplementICommand()
    {
        // Act
        var cmd = new CancelAllOrders();

        // Assert
        Assert.IsAssignableFrom<ICommand>(cmd);
    }
}

public class ModifyOrderTests
{
    [Fact]
    public void ModifyOrder_WithNewQuantity_ShouldStoreQuantity()
    {
        // Arrange
        var orderId = OrderId.New();
        var newQty = new Qty(200m);

        // Act
        var cmd = new ModifyOrder(orderId, NewQuantity: newQty);

        // Assert
        Assert.Equal(orderId, cmd.OrderId);
        Assert.Equal(newQty, cmd.NewQuantity);
        Assert.Null(cmd.NewLimitPrice);
    }

    [Fact]
    public void ModifyOrder_WithNewLimitPrice_ShouldStorePrice()
    {
        // Arrange
        var orderId = OrderId.New();
        var newPrice = new Price(155m);

        // Act
        var cmd = new ModifyOrder(orderId, NewLimitPrice: newPrice);

        // Assert
        Assert.Equal(orderId, cmd.OrderId);
        Assert.Null(cmd.NewQuantity);
        Assert.Equal(newPrice, cmd.NewLimitPrice);
    }

    [Fact]
    public void ModifyOrder_WithBoth_ShouldStoreBoth()
    {
        // Arrange
        var orderId = OrderId.New();
        var newQty = new Qty(300m);
        var newPrice = new Price(160m);

        // Act
        var cmd = new ModifyOrder(orderId, newQty, newPrice);

        // Assert
        Assert.Equal(orderId, cmd.OrderId);
        Assert.Equal(newQty, cmd.NewQuantity);
        Assert.Equal(newPrice, cmd.NewLimitPrice);
    }

    [Fact]
    public void ModifyOrder_ShouldImplementICommand()
    {
        // Act
        var cmd = new ModifyOrder(OrderId.New());

        // Assert
        Assert.IsAssignableFrom<ICommand>(cmd);
    }
}

public class SetPositionTests
{
    private static readonly Instrument TestInst = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void SetPosition_ShouldStoreTargetQuantity()
    {
        // Arrange
        var targetQty = new Qty(100m);

        // Act
        var cmd = new SetPosition(TestInst, targetQty);

        // Assert
        Assert.Equal(TestInst, cmd.Instrument);
        Assert.Equal(targetQty, cmd.TargetQuantity);
        Assert.Equal(OrderType.Market, cmd.OrderType);
    }

    [Fact]
    public void SetPosition_Flat_ShouldCreateFlatPosition()
    {
        // Act
        var cmd = SetPosition.Flat(TestInst);

        // Assert
        Assert.Equal(Qty.Zero, cmd.TargetQuantity);
    }

    [Fact]
    public void SetPosition_Long_ShouldCreateLongPosition()
    {
        // Arrange
        var qty = new Qty(100m);

        // Act
        var cmd = SetPosition.Long(TestInst, qty);

        // Assert
        Assert.Equal(qty, cmd.TargetQuantity);
    }

    [Fact]
    public void SetPosition_Short_ShouldCreateShortPosition()
    {
        // Arrange
        var qty = new Qty(100m);

        // Act
        var cmd = SetPosition.Short(TestInst, qty);

        // Assert
        Assert.Equal(-qty, cmd.TargetQuantity);
    }

    [Fact]
    public void SetPosition_WithLimitPrice_ShouldStoreLimitPrice()
    {
        // Arrange
        var limitPrice = new Price(150m);

        // Act
        var cmd = new SetPosition(TestInst, new Qty(100m), OrderType.Limit, limitPrice);

        // Assert
        Assert.Equal(OrderType.Limit, cmd.OrderType);
        Assert.Equal(limitPrice, cmd.LimitPrice);
    }

    [Fact]
    public void SetPosition_ShouldImplementICommand()
    {
        // Act
        var cmd = SetPosition.Flat(TestInst);

        // Assert
        Assert.IsAssignableFrom<ICommand>(cmd);
    }
}

public class SetAllocationTests
{
    private static readonly Instrument TestInst = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void SetAllocation_ShouldStoreTargetPercent()
    {
        // Act
        var cmd = new SetAllocation(TestInst, 0.25m);

        // Assert
        Assert.Equal(TestInst, cmd.Instrument);
        Assert.Equal(0.25m, cmd.TargetPercent);
        Assert.Equal(OrderType.Market, cmd.OrderType);
    }

    [Fact]
    public void SetAllocation_NegativePercent_ShouldAllowShort()
    {
        // Act
        var cmd = new SetAllocation(TestInst, -0.15m);

        // Assert
        Assert.Equal(-0.15m, cmd.TargetPercent);
    }

    [Fact]
    public void SetAllocation_WithLimitOrder_ShouldStoreOrderType()
    {
        // Act
        var cmd = new SetAllocation(TestInst, 0.30m, OrderType.Limit);

        // Assert
        Assert.Equal(OrderType.Limit, cmd.OrderType);
    }

    [Fact]
    public void SetAllocation_ShouldImplementICommand()
    {
        // Act
        var cmd = new SetAllocation(TestInst, 0.5m);

        // Assert
        Assert.IsAssignableFrom<ICommand>(cmd);
    }
}

public class LiquidateAllTests
{
    [Fact]
    public void LiquidateAll_ShouldHaveDefaultTag()
    {
        // Act
        var cmd = new LiquidateAll();

        // Assert
        Assert.Equal(0L, cmd.NumericTag);
    }

    [Fact]
    public void LiquidateAll_WithNumericTag_ShouldStoreTag()
    {
        // Act
        var cmd = new LiquidateAll(NumericTag: 999L);

        // Assert
        Assert.Equal(999L, cmd.NumericTag);
    }

    [Fact]
    public void LiquidateAll_ShouldImplementICommand()
    {
        // Act
        var cmd = new LiquidateAll();

        // Assert
        Assert.IsAssignableFrom<ICommand>(cmd);
    }
}

public class OrderTypeTests
{
    [Fact]
    public void OrderType_ShouldHaveByteBackingType()
    {
        // Assert
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(OrderType)));
    }

    [Fact]
    public void OrderType_ShouldHaveExpectedValues()
    {
        // Assert
        Assert.Equal(1, (byte)OrderType.Market);
        Assert.Equal(2, (byte)OrderType.Limit);
        Assert.Equal(3, (byte)OrderType.StopMarket);
        Assert.Equal(4, (byte)OrderType.StopLimit);
        Assert.Equal(5, (byte)OrderType.MarketIfTouched);
        Assert.Equal(6, (byte)OrderType.LimitIfTouched);
        Assert.Equal(7, (byte)OrderType.MarketToLimit);
        Assert.Equal(8, (byte)OrderType.TrailingStopMarket);
        Assert.Equal(9, (byte)OrderType.TrailingStopLimit);
    }
}

public class TimeInForceTests
{
    [Fact]
    public void TimeInForce_ShouldHaveByteBackingType()
    {
        // Assert
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(TimeInForce)));
    }

    [Fact]
    public void TimeInForce_ShouldHaveExpectedValues()
    {
        // Assert
        Assert.Equal(1, (byte)TimeInForce.Day);
        Assert.Equal(2, (byte)TimeInForce.GTC);
        Assert.Equal(3, (byte)TimeInForce.IOC);
        Assert.Equal(4, (byte)TimeInForce.FOK);
        Assert.Equal(5, (byte)TimeInForce.GTD);
    }
}

public class TrailingOffsetTypeTests
{
    [Fact]
    public void TrailingOffsetType_ShouldHaveByteBackingType()
    {
        // Assert
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(TrailingOffsetType)));
    }

    [Fact]
    public void TrailingOffsetType_ShouldHaveExpectedValues()
    {
        // Assert
        Assert.Equal(1, (byte)TrailingOffsetType.Price);
        Assert.Equal(2, (byte)TrailingOffsetType.Ticks);
        Assert.Equal(3, (byte)TrailingOffsetType.Percent);
    }
}

public class ContingencyTypeTests
{
    [Fact]
    public void ContingencyType_ShouldHaveByteBackingType()
    {
        // Assert
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(ContingencyType)));
    }

    [Fact]
    public void ContingencyType_ShouldHaveExpectedValues()
    {
        // Assert
        Assert.Equal(1, (byte)ContingencyType.OTO);
        Assert.Equal(2, (byte)ContingencyType.OCO);
        Assert.Equal(3, (byte)ContingencyType.OUO);
    }
}

public class OrderListIdTests
{
    [Fact]
    public void OrderListId_New_ShouldGenerate12CharacterId()
    {
        // Act
        var id = OrderListId.New();

        // Assert
        Assert.Equal(12, id.Value.Length);
    }

    [Fact]
    public void OrderListId_New_ShouldGenerateUniqueIds()
    {
        // Act
        var id1 = OrderListId.New();
        var id2 = OrderListId.New();

        // Assert
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void OrderListId_ImplicitConversion_ShouldWorkFromString()
    {
        // Arrange
        string value = "test123";

        // Act
        OrderListId id = value;

        // Assert
        Assert.Equal(value, id.Value);
    }

    [Fact]
    public void OrderListId_ToString_ShouldReturnValue()
    {
        // Arrange
        var id = new OrderListId("abc123");

        // Act
        var str = id.ToString();

        // Assert
        Assert.Equal("abc123", str);
    }
}
