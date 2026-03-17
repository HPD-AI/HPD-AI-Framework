using Rhodium.Primitives;

namespace Rhodium.Primitives.Tests;

public class OrderStatusTests
{
    [Fact]
    public void OrderStatus_ShouldHaveByteBackingType()
    {
        // Assert
        Assert.Equal(typeof(byte), Enum.GetUnderlyingType(typeof(OrderStatus)));
    }

    [Fact]
    public void OrderStatus_ShouldHaveExpectedValues()
    {
        // Assert
        Assert.Equal(0, (byte)OrderStatus.Pending);
        Assert.Equal(1, (byte)OrderStatus.Open);
        Assert.Equal(2, (byte)OrderStatus.PartiallyFilled);
        Assert.Equal(3, (byte)OrderStatus.Filled);
        Assert.Equal(4, (byte)OrderStatus.Cancelled);
        Assert.Equal(5, (byte)OrderStatus.Rejected);
        Assert.Equal(6, (byte)OrderStatus.Expired);
    }
}

public class QueuePositionTests
{
    [Fact]
    public void QueuePosition_ShouldStoreQtyAheadAndRelativePosition()
    {
        // Arrange & Act
        var queuePos = new QueuePosition(150.5m, 0.75m);

        // Assert
        Assert.Equal(150.5m, queuePos.QtyAhead);
        Assert.Equal(0.75m, queuePos.RelativePosition);
    }

    [Fact]
    public void QueuePosition_ShouldBeValueType()
    {
        // Arrange
        var pos1 = new QueuePosition(100m, 0.5m);
        var pos2 = new QueuePosition(100m, 0.5m);

        // Assert
        Assert.Equal(pos1, pos2);
    }
}

public class OrderTests
{
    private static readonly Instrument TestInst = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void Order_Empty_ShouldCreateEmptyOrder()
    {
        // Act
        var order = Order.Empty(TestInst);

        // Assert
        Assert.Equal(TestInst, order.Instrument);
        Assert.Equal(Side.None, order.Side);
        Assert.Equal(Qty.Zero, order.Quantity);
        Assert.Equal(OrderType.Market, order.Type);
    }

    [Fact]
    public void Order_ShouldStoreIdentityFields()
    {
        // Arrange
        var orderId = OrderId.New();
        var qty = new Qty(100m);

        // Act
        var order = new Order
        {
            Id = orderId,
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = qty,
            Type = OrderType.Limit
        };

        // Assert
        Assert.Equal(orderId, order.Id);
        Assert.Equal(TestInst, order.Instrument);
        Assert.Equal(Side.Buy, order.Side);
        Assert.Equal(qty, order.Quantity);
        Assert.Equal(OrderType.Limit, order.Type);
    }

    [Fact]
    public void Order_ShouldStorePrices()
    {
        // Arrange
        var limitPrice = new Price(150m);
        var stopPrice = new Price(145m);

        // Act
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.StopLimit,
            LimitPrice = limitPrice,
            StopPrice = stopPrice
        };

        // Assert
        Assert.Equal(limitPrice, order.LimitPrice);
        Assert.Equal(stopPrice, order.StopPrice);
    }

    [Fact]
    public void Order_TimeInForce_ShouldDefaultToDay()
    {
        // Act
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Assert
        Assert.Equal(TimeInForce.Day, order.TimeInForce);
    }

    [Fact]
    public void Order_ShouldStoreVariantIdAndNumericTag()
    {
        // Act
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market,
            VariantId = 42,
            NumericTag = 12345L
        };

        // Assert
        Assert.Equal(42, order.VariantId);
        Assert.Equal(12345L, order.NumericTag);
    }

    [Fact]
    public void Order_LimitPriceTick_ShouldConvertToTickPrice()
    {
        // Arrange
        var limitPrice = new Price(150.50m);

        // Act
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Limit,
            LimitPrice = limitPrice,
            TickSize = 0.01m
        };

        // Assert
        Assert.NotNull(order.LimitPriceTick);
        Assert.Equal(15050L, order.LimitPriceTick.Value.Ticks);
    }

    [Fact]
    public void Order_Status_ShouldDefaultToPending()
    {
        // Act
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Assert
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void Order_FilledQty_ShouldDefaultToZero()
    {
        // Act
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Assert
        Assert.Equal(Qty.Zero, order.FilledQty);
    }

    [Fact]
    public void Order_RemainingQty_ShouldCalculateCorrectly()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Act
        var fillPrice = new Price(150m);
        var commission = Money.USD(1m);
        order.Fill(new Qty(30m), fillPrice, commission, Instant.Now);

        // Assert
        Assert.Equal(new Qty(70m), order.RemainingQty);
    }

    [Fact]
    public void Order_IsOpen_ShouldReturnTrueForOpenStatuses()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Assert - Pending
        Assert.True(order.IsOpen);
        Assert.False(order.IsClosed);

        // Act - Accept
        order.Accept(Instant.Now);

        // Assert - Open
        Assert.True(order.IsOpen);
        Assert.False(order.IsClosed);
    }

    [Fact]
    public void Order_IsClosed_ShouldReturnTrueForClosedStatuses()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Act
        order.Cancel(Instant.Now);

        // Assert
        Assert.False(order.IsOpen);
        Assert.True(order.IsClosed);
    }

    [Fact]
    public void Order_FillPercent_ShouldCalculateCorrectly()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Act
        order.Fill(new Qty(25m), new Price(150m), Money.USD(0.5m), Instant.Now);

        // Assert
        Assert.Equal(0.25m, order.FillPercent);
    }

    [Fact]
    public void Order_IsTrailingStop_ShouldReturnTrueForTrailingStops()
    {
        // Arrange
        var trailingStop = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Sell,
            Quantity = new Qty(100m),
            Type = OrderType.TrailingStopMarket,
            TrailingOffset = 2.5m,
            TrailingOffsetType = Primitives.TrailingOffsetType.Price
        };

        var regular = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Assert
        Assert.True(trailingStop.IsTrailingStop);
        Assert.False(regular.IsTrailingStop);
    }

    [Fact]
    public void Order_IsPartOfOrderList_ShouldReturnTrueWhenOrderListIdSet()
    {
        // Arrange
        var orderListId = OrderListId.New();
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market,
            OrderListId = orderListId
        };

        // Assert
        Assert.True(order.IsPartOfOrderList);
    }

    [Fact]
    public void Order_UsesExecAlgorithm_ShouldReturnTrueWhenAlgorithmSet()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market,
            ExecAlgorithmId = "TWAP"
        };

        // Assert
        Assert.True(order.UsesExecAlgorithm);
    }

    [Fact]
    public void Order_Accept_ShouldSetStatusToOpen()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };
        var exchTime = Instant.Now;

        // Act
        order.Accept(exchTime);

        // Assert
        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.Equal(exchTime, order.ExchangeTimestamp);
    }

    [Fact]
    public void Order_Reject_ShouldSetStatusToRejected()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };
        var exchTime = Instant.Now;

        // Act
        order.Reject(exchTime);

        // Assert
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(exchTime, order.ExchangeTimestamp);
    }

    [Fact]
    public void Order_Fill_ShouldUpdateFilledQtyAndPrice()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };
        var fillPrice = new Price(150m);
        var commission = Money.USD(1m);
        var exchTime = Instant.Now;

        // Act
        order.Fill(new Qty(50m), fillPrice, commission, exchTime);

        // Assert
        Assert.Equal(new Qty(50m), order.FilledQty);
        Assert.Equal(fillPrice, order.AvgFillPrice);
        Assert.Equal(commission, order.TotalCommission);
        Assert.Equal(OrderStatus.PartiallyFilled, order.Status);
    }

    [Fact]
    public void Order_Fill_FullFill_ShouldSetStatusToFilled()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Act
        order.Fill(new Qty(100m), new Price(150m), Money.USD(1m), Instant.Now);

        // Assert
        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(new Qty(100m), order.FilledQty);
    }

    [Fact]
    public void Order_Fill_MultipleFills_ShouldCalculateAvgPrice()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Act - First fill: 50 @ 150
        order.Fill(new Qty(50m), new Price(150m), Money.USD(0.5m), Instant.Now);
        // Second fill: 50 @ 152
        order.Fill(new Qty(50m), new Price(152m), Money.USD(0.5m), Instant.Now);

        // Assert - Avg should be 151
        Assert.Equal(new Price(151m), order.AvgFillPrice);
        Assert.Equal(Money.USD(1m), order.TotalCommission);
        Assert.Equal(OrderStatus.Filled, order.Status);
    }

    [Fact]
    public void Order_Cancel_ShouldSetStatusToCancelled()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };
        var exchTime = Instant.Now;

        // Act
        order.Cancel(exchTime);

        // Assert
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(exchTime, order.ExchangeTimestamp);
    }

    [Fact]
    public void Order_Expire_ShouldSetStatusToExpired()
    {
        // Arrange
        var order = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };
        var exchTime = Instant.Now;

        // Act
        order.Expire(exchTime);

        // Assert
        Assert.Equal(OrderStatus.Expired, order.Status);
        Assert.Equal(exchTime, order.ExchangeTimestamp);
    }
}

public class OrderListTests
{
    private static readonly Instrument TestInst = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    private static Order CreateTestOrder(Side side = Side.Buy, OrderType type = OrderType.Market)
    {
        return new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = side,
            Quantity = new Qty(100m),
            Type = type
        };
    }

    [Fact]
    public void OrderList_Constructor_ShouldStoreAllFields()
    {
        // Arrange
        var id = OrderListId.New();
        var order1 = CreateTestOrder();
        var order2 = CreateTestOrder(Side.Sell);
        var orders = new[] { order1, order2 };

        // Act
        var orderList = new OrderList(id, ContingencyType.OCO, orders, TestInst);

        // Assert
        Assert.Equal(id, orderList.Id);
        Assert.Equal(ContingencyType.OCO, orderList.Contingency);
        Assert.Equal(2, orderList.Orders.Count);
        Assert.Equal(TestInst, orderList.Instrument);
    }

    [Fact]
    public void OrderList_Constructor_ShouldThrowIfLessThan2Orders()
    {
        // Arrange
        var order1 = CreateTestOrder();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new OrderList(OrderListId.New(), ContingencyType.OCO, new[] { order1 }, TestInst));
    }

    [Fact]
    public void OrderList_Constructor_ShouldThrowIfOrdersForDifferentInstruments()
    {
        // Arrange
        var order1 = CreateTestOrder();
        var order2 = new Order
        {
            Id = OrderId.New(),
            Instrument = new Instrument(new Asset("MSFT", AssetClass.Equity), Venue.NASDAQ),
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new OrderList(OrderListId.New(), ContingencyType.OCO, new[] { order1, order2 }, TestInst));
    }

    [Fact]
    public void OrderList_Create_ShouldGenerateId()
    {
        // Arrange
        var order1 = CreateTestOrder();
        var order2 = CreateTestOrder(Side.Sell);

        // Act
        var orderList = OrderList.Create(ContingencyType.OCO, TestInst, order1, order2);

        // Assert
        Assert.NotEqual(default(OrderListId), orderList.Id);
        Assert.Equal(ContingencyType.OCO, orderList.Contingency);
    }

    [Fact]
    public void OrderList_CreateOCO_ShouldCreateOCOPair()
    {
        // Arrange
        var stopLoss = CreateTestOrder(Side.Sell, OrderType.StopMarket);
        var takeProfit = CreateTestOrder(Side.Sell, OrderType.Limit);

        // Act
        var orderList = OrderList.CreateOCO(TestInst, stopLoss, takeProfit);

        // Assert
        Assert.Equal(ContingencyType.OCO, orderList.Contingency);
        Assert.Equal(2, orderList.Orders.Count);
    }

    [Fact]
    public void OrderList_CreateOTO_ShouldCreateOTOChain()
    {
        // Arrange
        var entry = CreateTestOrder(Side.Buy, OrderType.Limit);
        var stopLoss = CreateTestOrder(Side.Sell, OrderType.StopMarket);
        var takeProfit = CreateTestOrder(Side.Sell, OrderType.Limit);

        // Act
        var orderList = OrderList.CreateOTO(TestInst, entry, stopLoss, takeProfit);

        // Assert
        Assert.Equal(ContingencyType.OTO, orderList.Contingency);
        Assert.Equal(3, orderList.Orders.Count);
        Assert.Equal(entry, orderList.Orders[0]);
    }

    [Fact]
    public void OrderList_CreateBracket_ShouldCreateBracketOrder()
    {
        // Arrange
        var entry = CreateTestOrder(Side.Buy, OrderType.Market);
        var stopLoss = CreateTestOrder(Side.Sell, OrderType.StopMarket);
        var takeProfit = CreateTestOrder(Side.Sell, OrderType.Limit);

        // Act
        var orderList = OrderList.CreateBracket(TestInst, entry, stopLoss, takeProfit);

        // Assert
        Assert.Equal(ContingencyType.OTO, orderList.Contingency);
        Assert.Equal(3, orderList.Orders.Count);
    }
}

public class SubmitOrderListTests
{
    private static readonly Instrument TestInst = new(new Asset("AAPL", AssetClass.Equity), Venue.NASDAQ);

    [Fact]
    public void SubmitOrderList_ShouldStoreOrderList()
    {
        // Arrange
        var order1 = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };
        var order2 = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Sell,
            Quantity = new Qty(100m),
            Type = OrderType.Limit
        };
        var orderList = OrderList.CreateOCO(TestInst, order1, order2);

        // Act
        var cmd = new SubmitOrderList(orderList);

        // Assert
        Assert.Equal(orderList, cmd.OrderList);
    }

    [Fact]
    public void SubmitOrderList_Create_ShouldCreateCommand()
    {
        // Arrange
        var order1 = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };
        var order2 = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Sell,
            Quantity = new Qty(100m),
            Type = OrderType.Limit
        };
        var orderList = OrderList.CreateOCO(TestInst, order1, order2);

        // Act
        var cmd = SubmitOrderList.Create(orderList);

        // Assert
        Assert.Equal(orderList, cmd.OrderList);
    }

    [Fact]
    public void SubmitOrderList_ShouldImplementICommand()
    {
        // Arrange
        var order1 = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Buy,
            Quantity = new Qty(100m),
            Type = OrderType.Market
        };
        var order2 = new Order
        {
            Id = OrderId.New(),
            Instrument = TestInst,
            Side = Side.Sell,
            Quantity = new Qty(100m),
            Type = OrderType.Limit
        };
        var orderList = OrderList.CreateOCO(TestInst, order1, order2);

        // Act
        var cmd = new SubmitOrderList(orderList);

        // Assert
        Assert.IsAssignableFrom<ICommand>(cmd);
    }
}
