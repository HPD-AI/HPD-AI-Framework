using Rhodium.Tensor;

namespace Rhodium.HFT.Tests;

public sealed class L3EventHandlerTests
{
    [Fact]
    public void OrderAdd_WritesSlotAndAggregates()
    {
        using var store = new PagedTensorStore();
        var basis = new MarketTensorBasis(new MarketTensorSpaceConfig
        {
            InstrumentCount = 1,
            PriceLevelsPerInstrument = 4,
            OrderSlotsPerLevel = 3
        });
        var handler = new L3EventHandler(store, basis);

        handler.OnOrderAdd("SPY", 101, price: 500.25, size: 10.0, OrderSide.Buy, timestamp: 1234);

        var vi = basis.GetVI("SPY", priceLevel: 0, orderSlot: 0);
        Assert.Equal(101.0, store.GetScalar(MarketField.OrderId, vi).Value);
        Assert.Equal(10.0, store.GetScalar(MarketField.OrderQty, vi).Value);
        Assert.Equal(1234.0, store.GetScalar(MarketField.OrderTimestamp, vi).Value);
        Assert.Equal(10.0, store.GetScalar(MarketField.TotalQtyAtLevel, vi).Value);
        Assert.Equal(1.0, store.GetScalar(MarketField.OrderCount, vi).Value);
    }

    [Fact]
    public void MultipleAddsAtSamePrice_UseFifoSlotsAndUpdateAggregates()
    {
        using var store = new PagedTensorStore();
        var basis = new MarketTensorBasis(new MarketTensorSpaceConfig
        {
            InstrumentCount = 1,
            PriceLevelsPerInstrument = 4,
            OrderSlotsPerLevel = 3
        });
        var handler = new L3EventHandler(store, basis);

        handler.OnOrderAdd("SPY", 101, price: 500.25, size: 10.0, OrderSide.Buy, timestamp: 100);
        handler.OnOrderAdd("SPY", 102, price: 500.25, size: 15.0, OrderSide.Buy, timestamp: 101);

        var first = basis.GetVI("SPY", 0, 0);
        var second = basis.GetVI("SPY", 0, 1);
        Assert.Equal(101.0, store.GetScalar(MarketField.OrderId, first).Value);
        Assert.Equal(102.0, store.GetScalar(MarketField.OrderId, second).Value);
        Assert.Equal(25.0, store.GetScalar(MarketField.TotalQtyAtLevel, first).Value);
        Assert.Equal(2.0, store.GetScalar(MarketField.OrderCount, first).Value);
    }

    [Fact]
    public void ModifyExecuteAndDelete_UpdateSlotStateAndAggregates()
    {
        using var store = new PagedTensorStore();
        var basis = new MarketTensorBasis(new MarketTensorSpaceConfig
        {
            InstrumentCount = 1,
            PriceLevelsPerInstrument = 4,
            OrderSlotsPerLevel = 3
        });
        var handler = new L3EventHandler(store, basis);

        handler.OnOrderAdd("SPY", 101, price: 500.25, size: 10.0, OrderSide.Buy, timestamp: 100);
        var vi = basis.GetVI("SPY", 0, 0);

        handler.OnOrderModify("SPY", 101, newSize: 12.5);
        Assert.Equal(12.5, store.GetScalar(MarketField.OrderQty, vi).Value);
        Assert.Equal(12.5, store.GetScalar(MarketField.TotalQtyAtLevel, vi).Value);

        handler.OnOrderExecute("SPY", 101, executedSize: 2.5);
        Assert.Equal(10.0, store.GetScalar(MarketField.OrderQty, vi).Value);
        Assert.Equal(10.0, store.GetScalar(MarketField.TotalQtyAtLevel, vi).Value);

        handler.OnOrderDelete("SPY", 101);
        Assert.Equal(0.0, store.GetScalar(MarketField.OrderId, vi).Value);
        Assert.Equal(0.0, store.GetScalar(MarketField.OrderQty, vi).Value);
        Assert.Equal(0.0, store.GetScalar(MarketField.TotalQtyAtLevel, vi).Value);
        Assert.Equal(0.0, store.GetScalar(MarketField.OrderCount, vi).Value);
    }
}
