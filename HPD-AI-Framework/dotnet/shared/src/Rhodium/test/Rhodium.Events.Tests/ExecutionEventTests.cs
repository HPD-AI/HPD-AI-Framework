using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Events.Tests;

public class OrderAcceptedTests
{
    [Fact]
    public void OrderAccepted_ShouldStoreOrderIdStrategyIdAndVariantId()
    {
        // Arrange
        var orderId = OrderId.New();
        var strategyId = new StrategyId(7);
        var variantId = 42;

        // Act
        var evt = new OrderAccepted(orderId, strategyId, variantId);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(variantId, evt.VariantId);
    }

    [Fact]
    public void OrderAccepted_ShouldBeExecutionEvent()
    {
        // Arrange & Act
        var evt = new OrderAccepted(OrderId.New(), new StrategyId(1), 1);

        // Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
        Assert.IsAssignableFrom<FinanceEvent>(evt);
    }

    [Fact]
    public void OrderAccepted_ShouldHaveSynchronousChannel()
    {
        // Arrange & Act
        var evt = new OrderAccepted(OrderId.New(), new StrategyId(1), 1);

        // Assert
        Assert.Equal(HPD.Events.EventChannel.Synchronous, evt.Channel);
    }

    [Fact]
    public void OrderAccepted_ShouldStoreExplicitAssetIdWhenProvided()
    {
        var assetId = new AssetId(17);

        var evt = new OrderAccepted(OrderId.New(), new StrategyId(1), VariantId: 1, AssetId: assetId);

        Assert.Equal(assetId, evt.AssetId);
    }
}

public class OrderModifiedTests
{
    [Fact]
    public void OrderModified_ShouldStoreOrderIdStrategyIdVariantIdAndReplacementFields()
    {
        var orderId = OrderId.New();
        var strategyId = new StrategyId(7);
        var qty = new Qty(2m);
        var limitPrice = new Price(101m, Currency.USD);

        var evt = new OrderModified(orderId, strategyId, VariantId: 3, qty, limitPrice);

        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(3, evt.VariantId);
        Assert.Equal(qty, evt.NewQuantity);
        Assert.Equal(limitPrice, evt.NewLimitPrice);
    }

    [Fact]
    public void OrderModified_ShouldStoreExplicitAssetIdWhenProvided()
    {
        var assetId = new AssetId(18);

        var evt = new OrderModified(OrderId.New(), new StrategyId(1), VariantId: 3, AssetId: assetId);

        Assert.Equal(assetId, evt.AssetId);
    }

    [Fact]
    public void OrderModified_ShouldBeExecutionEvent()
    {
        var evt = new OrderModified(OrderId.New(), new StrategyId(1), VariantId: 0);

        Assert.IsAssignableFrom<ExecutionEvent>(evt);
        Assert.IsAssignableFrom<FinanceEvent>(evt);
    }
}

public class OrderRejectedTests
{
    [Fact]
    public void OrderRejected_ShouldStoreOrderIdStrategyIdVariantIdAndReason()
    {
        // Arrange
        var orderId = OrderId.New();
        var strategyId = new StrategyId(3);
        var variantId = 10;
        var reason = "Insufficient funds";

        // Act
        var evt = new OrderRejected(orderId, strategyId, variantId, reason);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(variantId, evt.VariantId);
        Assert.Equal(reason, evt.Reason);
    }

    [Fact]
    public void OrderRejected_ShouldStoreExplicitAssetIdWhenProvided()
    {
        var assetId = new AssetId(19);

        var evt = new OrderRejected(OrderId.New(), new StrategyId(1), VariantId: 3, "bad price", assetId);

        Assert.Equal(assetId, evt.AssetId);
    }

    [Fact]
    public void OrderRejected_ShouldBeExecutionEvent()
    {
        // Arrange & Act
        var evt = new OrderRejected(OrderId.New(), new StrategyId(1), 1, "Market closed");

        // Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
    }
}

public class OrderFilledTests
{
    [Fact]
    public void OrderFilled_ShouldStoreAllOrderDetails()
    {
        // Arrange
        var orderId = OrderId.New();
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var variantId = 5;
        var strategyId = new StrategyId(4);
        var side = Side.Buy;
        var qty = new Qty(100m);
        var price = new Price(150.50m, Currency.USD);
        var commission = Money.USD(1.50m);

        // Act
        var evt = new OrderFilled(orderId, instrument, variantId, strategyId, side, qty, price, commission);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(instrument, evt.Instrument);
        Assert.Equal(variantId, evt.VariantId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(side, evt.Side);
        Assert.Equal(qty, evt.FilledQty);
        Assert.Equal(price, evt.FillPrice);
        Assert.Equal(commission, evt.Commission);
        Assert.Null(evt.AssetId);
    }

    [Fact]
    public void OrderFilled_ShouldStoreExplicitAssetIdWhenProvided()
    {
        var instrument = new Instrument(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);
        var assetId = new AssetId(17);

        var evt = new OrderFilled(
            OrderId.New(),
            instrument,
            VariantId: 5,
            new StrategyId(4),
            Side.Buy,
            new Qty(100m),
            new Price(150.50m, Currency.USD),
            Money.USD(1.50m),
            AssetId: assetId);

        Assert.Equal(assetId, evt.AssetId);
    }

    [Fact]
    public void OrderFilled_Value_ShouldCalculateCorrectly()
    {
        // Arrange
        var orderId = OrderId.New();
        var instrument = new Instrument(new Asset("MSFT", AssetClass.Equity), Venue.NASDAQ);
        var qty = new Qty(50m);
        var price = new Price(300m, Currency.USD);
        var commission = Money.USD(2m);

        // Act
        var evt = new OrderFilled(orderId, instrument, 1, new StrategyId(1), Side.Buy, qty, price, commission);

        // Assert
        // Value = 50 * 300 = 15000
        Assert.Equal(15000m, evt.Value.Amount);
        Assert.Equal(Currency.USD, evt.Value.Currency);
    }

    [Fact]
    public void OrderFilled_ShouldBeExecutionEvent()
    {
        // Arrange
        var evt = new OrderFilled(
            OrderId.New(),
            new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            1,
            new StrategyId(1),
            Side.Sell,
            new Qty(0.5m),
            new Price(50000m),
            Money.USD(25m)
        );

        // Act & Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
    }

    [Fact]
    public void OrderFilled_Time_ShouldBeInitSettable()
    {
        var fillTime = new Instant(1_700_000_000_987_654_321L);

        var evt = new OrderFilled(
            OrderId.New(),
            new Instrument(new Asset("BTC", AssetClass.Crypto), Venue.Binance),
            1,
            new StrategyId(1),
            Side.Sell,
            new Qty(0.5m),
            new Price(50000m),
            Money.USD(25m))
        {
            Time = fillTime
        };

        Assert.Equal(fillTime, evt.Time);
        Assert.Equal(fillTime.Nanos, evt.ExchangeTimestampNs);
        Assert.Equal(fillTime.ToDateTimeOffset(), evt.Timestamp);
    }
}

public class OrderCancelledTests
{
    [Fact]
    public void OrderCancelled_ShouldStoreOrderIdStrategyIdVariantIdRemainingQtyAndReason()
    {
        // Arrange
        var orderId = OrderId.New();
        var strategyId = new StrategyId(5);
        var variantId = 3;
        var remainingQty = new Qty(75m);
        var reason = "User cancelled";

        // Act
        var evt = new OrderCancelled(orderId, strategyId, variantId, remainingQty, reason);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(variantId, evt.VariantId);
        Assert.Equal(remainingQty, evt.RemainingQty);
        Assert.Equal(reason, evt.Reason);
    }

    [Fact]
    public void OrderCancelled_ShouldStoreExplicitAssetIdWhenProvided()
    {
        var assetId = new AssetId(20);

        var evt = new OrderCancelled(OrderId.New(), new StrategyId(1), VariantId: 3, new Qty(1m), "user", AssetId: assetId);

        Assert.Equal(assetId, evt.AssetId);
    }

    [Fact]
    public void OrderCancelled_ShouldBeExecutionEvent()
    {
        // Arrange & Act
        var evt = new OrderCancelled(OrderId.New(), new StrategyId(1), 1, new Qty(50m), "Timeout");

        // Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
    }
}

public class OrderExpiredTests
{
    [Fact]
    public void OrderExpired_ShouldStoreOrderIdStrategyIdAndVariantId()
    {
        // Arrange
        var orderId = OrderId.New();
        var strategyId = new StrategyId(9);
        var variantId = 7;

        // Act
        var evt = new OrderExpired(orderId, strategyId, variantId);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(strategyId, evt.StrategyId);
        Assert.Equal(variantId, evt.VariantId);
    }

    [Fact]
    public void OrderExpired_ShouldStoreExplicitAssetIdWhenProvided()
    {
        var assetId = new AssetId(21);

        var evt = new OrderExpired(OrderId.New(), new StrategyId(1), VariantId: 7, AssetId: assetId);

        Assert.Equal(assetId, evt.AssetId);
    }

    [Fact]
    public void OrderExpired_ShouldBeExecutionEvent()
    {
        // Arrange & Act
        var evt = new OrderExpired(OrderId.New(), new StrategyId(1), 1);

        // Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
    }
}
