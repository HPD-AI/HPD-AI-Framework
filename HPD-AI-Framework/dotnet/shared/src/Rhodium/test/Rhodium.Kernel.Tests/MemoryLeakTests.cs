using Rhodium.Primitives;
using Rhodium.Tensor;
using Rhodium.Unsafe;
using Rhodium.Unsafe.Storage;

namespace Rhodium.Kernel.Tests;

public class MemoryLeakTests
{
    [Fact]
    [Trait("Category", "MemoryLeak")]
    public void Runtime_Dispose_ReleasesTrackedTensorAndWorldStateMemory()
    {
        var tracker = new GlobalMemoryTracker();

        using (var runtime = new RhodiumRuntime(tracker))
        {
            runtime.BatchMap.AddInstrument(new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NYSE));
            runtime.Tensors.GetScalar(Field.Close, runtime.Tensors.Grow()) = new PriceF64(100);
            runtime.WorldState.PositionAt(new StrategyId(1), 0).ApplyFill(
                Side.Buy,
                new Qty(1m),
                new Price(100m, Currency.USD),
                Money.USD(0m));
        }

        tracker.AssertNoLeaks();
    }

    [Fact]
    [Trait("Category", "MemoryLeak")]
    public void AlignedPagePool_DetectsDoubleReturn()
    {
        using var pool = new AlignedPagePool<double>();
        var page = pool.Rent();

        pool.Return(page);

        Assert.Throws<InvalidOperationException>(() => pool.Return(page));
    }

    [Fact]
    [Trait("Category", "MemoryLeak")]
    public void AlignedPagePool_DetectsSamePageReturnedTwiceAfterMultipleRents()
    {
        using var pool = new AlignedPagePool<double>();
        var first = pool.Rent();
        _ = pool.Rent();

        pool.Return(first);

        Assert.Throws<InvalidOperationException>(() => pool.Return(first));
    }
}
