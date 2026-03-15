using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Events.Tests;

public class OrderAcceptedTests
{
    [Fact]
    public void OrderAccepted_ShouldStoreOrderIdAndVariantId()
    {
        // Arrange
        var orderId = OrderId.New();
        var variantId = 42;

        // Act
        var evt = new OrderAccepted(orderId, variantId);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(variantId, evt.VariantId);
    }

    [Fact]
    public void OrderAccepted_ShouldBeExecutionEvent()
    {
        // Arrange & Act
        var evt = new OrderAccepted(OrderId.New(), 1);

        // Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
        Assert.IsAssignableFrom<FinanceEvent>(evt);
    }

    [Fact]
    public void OrderAccepted_ShouldHaveControlPriority()
    {
        // Arrange & Act
        var evt = new OrderAccepted(OrderId.New(), 1);

        // Assert
        Assert.Equal(HPD.Events.EventPriority.Control, evt.Priority);
    }
}

public class OrderRejectedTests
{
    [Fact]
    public void OrderRejected_ShouldStoreOrderIdVariantIdAndReason()
    {
        // Arrange
        var orderId = OrderId.New();
        var variantId = 10;
        var reason = "Insufficient funds";

        // Act
        var evt = new OrderRejected(orderId, variantId, reason);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(variantId, evt.VariantId);
        Assert.Equal(reason, evt.Reason);
    }

    [Fact]
    public void OrderRejected_ShouldBeExecutionEvent()
    {
        // Arrange & Act
        var evt = new OrderRejected(OrderId.New(), 1, "Market closed");

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
        var side = Side.Buy;
        var qty = new Qty(100m);
        var price = new Price(150.50m, Currency.USD);
        var commission = Money.USD(1.50m);

        // Act
        var evt = new OrderFilled(orderId, instrument, variantId, side, qty, price, commission);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(instrument, evt.Instrument);
        Assert.Equal(variantId, evt.VariantId);
        Assert.Equal(side, evt.Side);
        Assert.Equal(qty, evt.FilledQty);
        Assert.Equal(price, evt.FillPrice);
        Assert.Equal(commission, evt.Commission);
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
        var evt = new OrderFilled(orderId, instrument, 1, Side.Buy, qty, price, commission);

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
            Side.Sell,
            new Qty(0.5m),
            new Price(50000m),
            Money.USD(25m)
        );

        // Act & Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
    }
}

public class OrderCancelledTests
{
    [Fact]
    public void OrderCancelled_ShouldStoreOrderIdVariantIdRemainingQtyAndReason()
    {
        // Arrange
        var orderId = OrderId.New();
        var variantId = 3;
        var remainingQty = new Qty(75m);
        var reason = "User cancelled";

        // Act
        var evt = new OrderCancelled(orderId, variantId, remainingQty, reason);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(variantId, evt.VariantId);
        Assert.Equal(remainingQty, evt.RemainingQty);
        Assert.Equal(reason, evt.Reason);
    }

    [Fact]
    public void OrderCancelled_ShouldBeExecutionEvent()
    {
        // Arrange & Act
        var evt = new OrderCancelled(OrderId.New(), 1, new Qty(50m), "Timeout");

        // Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
    }
}

public class OrderExpiredTests
{
    [Fact]
    public void OrderExpired_ShouldStoreOrderIdAndVariantId()
    {
        // Arrange
        var orderId = OrderId.New();
        var variantId = 7;

        // Act
        var evt = new OrderExpired(orderId, variantId);

        // Assert
        Assert.Equal(orderId, evt.OrderId);
        Assert.Equal(variantId, evt.VariantId);
    }

    [Fact]
    public void OrderExpired_ShouldBeExecutionEvent()
    {
        // Arrange & Act
        var evt = new OrderExpired(OrderId.New(), 1);

        // Assert
        Assert.IsAssignableFrom<ExecutionEvent>(evt);
    }
}
