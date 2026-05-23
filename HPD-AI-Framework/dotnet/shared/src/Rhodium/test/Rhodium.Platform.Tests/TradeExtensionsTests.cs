using Rhodium.Kernel;
using Rhodium.Platform.Extensions;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

public class TradeExtensionsTests
{
    [Fact]
    public void Buy_IncreasesPosition()
    {
        using var runtime = CreateRuntimeWithClose(100);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(new StrategyId(1), null, default, CreateCounters(), commands);

        portfolio.Buy(new AssetId(0), new Qty(10m), in market);

        Assert.Equal(10m, portfolio.GetPositionQty(new AssetId(0)));
    }

    [Fact]
    public void Sell_DecreasesPosition()
    {
        using var runtime = CreateRuntimeWithClose(100);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(new StrategyId(1), null, default, CreateCounters(), commands);

        portfolio.Sell(new AssetId(0), new Qty(5m), in market);

        Assert.Equal(-5m, portfolio.GetPositionQty(new AssetId(0)));
    }

    [Fact]
    public void SetPosition_AppliesDelta()
    {
        using var runtime = CreateRuntimeWithClose(100);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(new StrategyId(1), null, default, CreateCounters(), commands);

        portfolio.SetPosition(new AssetId(0), new Qty(12m), in market);
        portfolio.SetPosition(new AssetId(0), new Qty(3m), in market);

        Assert.Equal(3m, portfolio.GetPositionQty(new AssetId(0)));
    }

    [Fact]
    public void Flatten_ClosesPosition()
    {
        using var runtime = CreateRuntimeWithClose(100);
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var portfolio = runtime.WorldState.BuildContext(new StrategyId(1), null, default, CreateCounters(), commands);

        portfolio.Buy(new AssetId(0), new Qty(10m), in market);
        portfolio.Flatten(new AssetId(0), in market);

        Assert.Equal(0m, portfolio.GetPositionQty(new AssetId(0)));
    }

    private static RhodiumRuntime CreateRuntimeWithClose(double close)
    {
        var runtime = new RhodiumRuntime();
        var inst = new Instrument(new Asset("TEST", AssetClass.Equity), Venue.NASDAQ);
        runtime.BatchMap.AddInstrument(inst);
        runtime.Tensors.Grow();
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(close);
        runtime.SetMetadata(0, SecurityMetadata.Equity(inst));
        return runtime;
    }

    private static int[] CreateCounters() => new int[PortfolioContext.CounterCount];
}
