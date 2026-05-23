using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Quant;
using Rhodium.Tensor;

namespace Rhodium.Quant.Tests;

public sealed class SnapshotManagerTests
{
    [Fact]
    public void TakeSnapshot_CapturesPortfolioAndBatchMapVersion()
    {
        using var runtime = CreateRuntime();
        var strategyId = new StrategyId(10);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(strategyId, null, default, CreateCounters(), commands);
        portfolio.Buy(new AssetId(0), new Qty(2m), in market);

        using var manager = new SnapshotManager();
        using var snapshot = manager.TakeSnapshot(in market, runtime.WorldState, strategyId);

        Assert.NotNull(snapshot);
        Assert.Equal(runtime.BatchMap.Version, snapshot.BatchMapVersion);
        Assert.Equal(strategyId, snapshot.StrategyId);
        Assert.Equal(1, snapshot.UniverseSize);
        Assert.Equal(strategyId, snapshot.Portfolio.StrategyId);
        Assert.Equal(200m, snapshot.Portfolio.GrossExposure);
        Assert.Equal(100.0, snapshot.MarketData.Close[0]);
    }

    [Fact]
    public void TakeSnapshot_CopiesMarketDataRatherThanReferencingLiveTensor()
    {
        using var runtime = CreateRuntime();
        var strategyId = new StrategyId(12);
        var market = runtime.CreateMarketKernel();
        using var manager = new SnapshotManager();

        using var snapshot = manager.TakeSnapshot(in market, runtime.WorldState, strategyId);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(200);

        Assert.NotNull(snapshot);
        Assert.Equal(100.0, snapshot.MarketData.Close[0]);
        Assert.Equal(200.0, market.GetScalar(Field.Close, new AssetId(0)));
    }

    [Fact]
    public void TakeSnapshot_ReturnsNullWhenPoolIsExhaustedUntilRelease()
    {
        using var runtime = CreateRuntime();
        var strategyId = new StrategyId(11);
        var market = runtime.CreateMarketKernel();
        using var manager = new SnapshotManager(maxPoolSize: 1);

        var first = manager.TakeSnapshot(in market, runtime.WorldState, strategyId);
        var second = manager.TakeSnapshot(in market, runtime.WorldState, strategyId);

        Assert.NotNull(first);
        Assert.Null(second);

        first.Dispose();
        using var third = manager.TakeSnapshot(in market, runtime.WorldState, strategyId);

        Assert.NotNull(third);
    }

    private static RhodiumRuntime CreateRuntime()
    {
        var runtime = new RhodiumRuntime();
        runtime.BatchMap.AddInstrument(new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ));
        runtime.Tensors.Grow();
        runtime.Tensors.GetScalar(Field.Open, 0) = new PriceF64(99);
        runtime.Tensors.GetScalar(Field.High, 0) = new PriceF64(101);
        runtime.Tensors.GetScalar(Field.Low, 0) = new PriceF64(98);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        runtime.Tensors.GetScalar(Field.Volume, 0) = new SizeF64(1000);
        return runtime;
    }

    private static int[] CreateCounters() => new int[PortfolioContext.CounterCount];
}
