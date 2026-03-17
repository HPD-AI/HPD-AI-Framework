using Rhodium.HFT;
using Rhodium.Tensor;

namespace Rhodium.HFT.Tests;

public class MarketFieldTests
{
    [Fact]
    public void MarketField_OrderIdHasCorrectName()
    {
        Assert.Equal("market.order_id", MarketField.OrderId.Name);
    }

    [Fact]
    public void MarketField_OrderQtyHasCorrectName()
    {
        Assert.Equal("market.order_qty", MarketField.OrderQty.Name);
    }

    [Fact]
    public void MarketField_OrderTimestampHasCorrectName()
    {
        Assert.Equal("market.order_ts", MarketField.OrderTimestamp.Name);
    }

    [Fact]
    public void MarketField_TotalQtyAtLevelHasCorrectName()
    {
        Assert.Equal("market.total_qty", MarketField.TotalQtyAtLevel.Name);
    }

    [Fact]
    public void MarketField_OrderCountHasCorrectName()
    {
        Assert.Equal("market.order_count", MarketField.OrderCount.Name);
    }

    [Fact]
    public void MarketField_OrderIdIsFactorF64Type()
    {
        using var store = new PagedTensorStore();
        store.Grow();

        // Should be able to store FactorF64
        store.GetScalar(MarketField.OrderId, 0) = new FactorF64(12345.0);
        Assert.Equal(12345.0, store.GetScalar(MarketField.OrderId, 0).Value);
    }

    [Fact]
    public void MarketField_OrderQtyIsSizeF64Type()
    {
        using var store = new PagedTensorStore();
        store.Grow();

        store.GetScalar(MarketField.OrderQty, 0) = new SizeF64(1000.0);
        Assert.Equal(1000.0, store.GetScalar(MarketField.OrderQty, 0).Value);
    }

    [Fact]
    public void MarketField_OrderTimestampIsFactorF64Type()
    {
        using var store = new PagedTensorStore();
        store.Grow();

        store.GetScalar(MarketField.OrderTimestamp, 0) = new FactorF64(1000000000.0);
        Assert.Equal(1000000000.0, store.GetScalar(MarketField.OrderTimestamp, 0).Value);
    }

    [Fact]
    public void MarketField_TotalQtyAtLevelIsSizeF64Type()
    {
        using var store = new PagedTensorStore();
        store.Grow();

        store.GetScalar(MarketField.TotalQtyAtLevel, 0) = new SizeF64(5000.0);
        Assert.Equal(5000.0, store.GetScalar(MarketField.TotalQtyAtLevel, 0).Value);
    }

    [Fact]
    public void MarketField_OrderCountIsFactorF64Type()
    {
        using var store = new PagedTensorStore();
        store.Grow();

        store.GetScalar(MarketField.OrderCount, 0) = new FactorF64(10.0);
        Assert.Equal(10.0, store.GetScalar(MarketField.OrderCount, 0).Value);
    }

    [Fact]
    public void MarketField_SimulateL3OrderBookSlot()
    {
        using var store = new PagedTensorStore();
        var virtualIndex = store.Grow();

        // Simulate an order at a specific slot
        store.GetScalar(MarketField.OrderId, virtualIndex) = new FactorF64(999888777.0);
        store.GetScalar(MarketField.OrderQty, virtualIndex) = new SizeF64(100.0);
        store.GetScalar(MarketField.OrderTimestamp, virtualIndex) = new FactorF64(1700000000000.0);

        // Simulate aggregated level stats
        store.GetScalar(MarketField.TotalQtyAtLevel, virtualIndex) = new SizeF64(500.0);
        store.GetScalar(MarketField.OrderCount, virtualIndex) = new FactorF64(5.0);

        // Verify all fields
        Assert.Equal(999888777.0, store.GetScalar(MarketField.OrderId, virtualIndex).Value);
        Assert.Equal(100.0, store.GetScalar(MarketField.OrderQty, virtualIndex).Value);
        Assert.Equal(1700000000000.0, store.GetScalar(MarketField.OrderTimestamp, virtualIndex).Value);
        Assert.Equal(500.0, store.GetScalar(MarketField.TotalQtyAtLevel, virtualIndex).Value);
        Assert.Equal(5.0, store.GetScalar(MarketField.OrderCount, virtualIndex).Value);
    }

    [Fact]
    public void MarketField_BroadcastAcrossMultipleSlots()
    {
        using var store = new PagedTensorStore();

        // Grow 100 slots (e.g., 100 order slots at a price level)
        for (int i = 0; i < 100; i++)
            store.Grow();

        // Clear all order IDs (0 = empty slot)
        store.Broadcast(MarketField.OrderId, new FactorF64(0.0), 0, 100);

        // Verify all cleared
        for (int i = 0; i < 100; i++)
            Assert.Equal(0.0, store.GetScalar(MarketField.OrderId, i).Value);
    }

    [Fact]
    public void MarketField_MultipleFieldsAreIndependent()
    {
        using var store = new PagedTensorStore();
        store.Grow();

        store.GetScalar(MarketField.OrderId, 0) = new FactorF64(123.0);
        store.GetScalar(MarketField.OrderQty, 0) = new SizeF64(456.0);

        // Fields should not interfere with each other
        Assert.Equal(123.0, store.GetScalar(MarketField.OrderId, 0).Value);
        Assert.Equal(456.0, store.GetScalar(MarketField.OrderQty, 0).Value);
    }
}
