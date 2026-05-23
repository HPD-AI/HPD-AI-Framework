using Rhodium.HFT;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel.Tests;

public class RhodiumRuntimeTests
{
    [Fact]
    public void Runtime_Constructor_InitializesCoreOwners()
    {
        using var runtime = new RhodiumRuntime();

        Assert.NotNull(runtime.Tensors);
        Assert.NotNull(runtime.MarketState);
        Assert.NotNull(runtime.BatchMap);
        Assert.NotNull(runtime.MarketBatchMap);
        Assert.NotNull(runtime.WorldState);
    }

    [Fact]
    public void MarketKernel_ReadsMetadataFromRuntime()
    {
        using var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE);
        var meta = SecurityMetadata.Equity(inst, tickSize: 0.05m);

        runtime.SetMetadata(0, meta);
        var market = runtime.CreateMarketKernel();

        Assert.Equal(0.05m, market.GetMetadata(new AssetId(0)).TickSize);
        Assert.Equal(Currency.USD, market.GetMetadata(new AssetId(0)).Currency);
    }

    [Fact]
    public void MarketKernel_ReadsDepthFromRuntime()
    {
        using var runtime = new RhodiumRuntime();
        var depth = new HashMapDepth(0.01m, 1m);

        depth.Update(Side.Buy, 10000, 100m, Instant.Now);
        depth.Update(Side.Sell, 10001, 50m, Instant.Now);
        runtime.SetDepth(0, depth);

        var market = runtime.CreateMarketKernel();

        Assert.Equal(10000L, market.GetBestBidTick(new AssetId(0)));
        Assert.Equal(10001L, market.GetBestAskTick(new AssetId(0)));
        Assert.Equal(100m, market.GetQtyAtTick(new AssetId(0), Side.Buy, 10000));
    }

    [Fact]
    public void WorldState_RoutesPositionsByStrategyId()
    {
        using var runtime = new RhodiumRuntime();
        var a = new StrategyId(1);
        var b = new StrategyId(2);

        runtime.WorldState.PositionAt(a, 0).Quantity = 10m;
        runtime.WorldState.PositionAt(b, 0).Quantity = 20m;

        Assert.Equal(10m, runtime.WorldState.PositionAt(a, 0).Quantity);
        Assert.Equal(20m, runtime.WorldState.PositionAt(b, 0).Quantity);
    }

    [Fact]
    public void MarketKernel_CanReadTensorScalar()
    {
        using var runtime = new RhodiumRuntime();
        runtime.Tensors.Grow();
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(101.25);

        var market = runtime.CreateMarketKernel();

        Assert.Equal(101.25, market.GetScalar(Field.Close, new AssetId(0)));
    }
}
