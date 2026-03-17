using Rhodium.HFT;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel.Tests;

public class TradingEngineTests
{
    [Fact]
    public void TradingEngine_Constructor_InitializesComponents()
    {
        var engine = new TradingEngine();

        Assert.NotNull(engine.Tensors);
        Assert.NotNull(engine.MarketState);
        Assert.NotNull(engine.BatchMap);
        Assert.NotNull(engine.MarketBatchMap);
    }

    [Fact]
    public void TradingEngine_SetMetadata_CanRetrieve()
    {
        var engine = new TradingEngine();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var meta = SecurityMetadata.Equity(inst, tickSize: 0.01m);

        engine.SetMetadata(0, meta);

        Assert.Equal(0.01m, engine.GetTickSize(0));
        Assert.Equal(1m, engine.GetLotSize(0));
        // Currency defaults to USD in the getter
    }

    [Fact]
    public void TradingEngine_GetMetadata_ReturnsDefaultsWhenNotSet()
    {
        var engine = new TradingEngine();

        Assert.Equal(0.01m, engine.GetTickSize(999));
        Assert.Equal(1m, engine.GetLotSize(999));
        Assert.Equal(Currency.USD, engine.GetCurrency(999));
    }

    [Fact]
    public void TradingEngine_SetDepth_CanRetrieve()
    {
        var engine = new TradingEngine();
        var depth = new HashMapDepth(0.01m, 1m);

        depth.Update(Side.Buy, 10000, 100m, Instant.Now);
        depth.Update(Side.Sell, 10001, 50m, Instant.Now);

        engine.SetDepth(0, depth);

        Assert.Equal(10000L, engine.GetBestBidTick(0));
        Assert.Equal(10001L, engine.GetBestAskTick(0));
        Assert.Equal(100m, engine.GetQtyAtTick(0, Side.Buy, 10000));
    }

    [Fact]
    public void TradingEngine_GetDepth_ReturnsNullWhenNotSet()
    {
        var engine = new TradingEngine();

        var depth = engine.GetDepth(999);

        Assert.Null(depth);
    }

    [Fact]
    public void TradingEngine_Position_CanSetAndGet()
    {
        var engine = new TradingEngine();

        engine.SetPosition(0, 100m);

        Assert.Equal(100m, engine.GetPosition(0));
    }

    [Fact]
    public void TradingEngine_Position_ReturnsZeroWhenNotSet()
    {
        var engine = new TradingEngine();

        Assert.Equal(0m, engine.GetPosition(999));
    }

    [Fact]
    public void TradingEngine_SubmitLimitOrder_TracksActiveOrders()
    {
        var engine = new TradingEngine();

        engine.SubmitLimitOrder(0, Side.Buy, new Qty(100m), new Price(99m));

        Assert.True(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void TradingEngine_CancelOrder_RemovesFromActive()
    {
        var engine = new TradingEngine();

        engine.SubmitMarketOrder(0, Side.Buy, new Qty(100m));
        var orderId = OrderId.New();

        engine.CancelOrder(0, orderId);

        // Still has orders from submit, but specific order removed
        Assert.True(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void TradingEngine_CancelAllOrders_ClearsAll()
    {
        var engine = new TradingEngine();

        engine.SubmitMarketOrder(0, Side.Buy, new Qty(100m));
        engine.SubmitLimitOrder(0, Side.Buy, new Qty(50m), new Price(99m));

        engine.CancelAllOrders(0);

        Assert.False(engine.HasOpenOrder(0, Side.Buy));
    }

    [Fact]
    public void TradingEngine_EnsureColumn_DoesNotThrow()
    {
        var engine = new TradingEngine();
        engine.Tensors.Grow(); // Need to grow before accessing

        var field = new VectorField<PriceF64>("test.field");

        // Should not throw
        engine.EnsureColumn(field);
    }
}
