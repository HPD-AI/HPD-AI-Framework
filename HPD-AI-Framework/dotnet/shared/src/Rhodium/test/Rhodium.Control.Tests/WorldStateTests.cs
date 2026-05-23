using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Control.Tests;

public class WorldStateTests
{
    [Fact]
    public void WorldState_PositionAt_IsPerStrategy()
    {
        using var state = new WorldState();
        var a = new StrategyId(1);
        var b = new StrategyId(2);

        state.PositionAt(a, 10).ApplyFill(Side.Buy, new Qty(50m), new Price(100m), Money.Zero(Currency.USD));
        state.PositionAt(b, 10).ApplyFill(Side.Buy, new Qty(100m), new Price(100m), Money.Zero(Currency.USD));

        Assert.Equal(50m, state.PositionAt(a, 10).Quantity);
        Assert.Equal(100m, state.PositionAt(b, 10).Quantity);
    }

    [Fact]
    public void WorldState_MultiplePages_AreAllocatedOnDemand()
    {
        using var state = new WorldState();
        var id = new StrategyId(1);

        state.PositionAt(id, 1500).ApplyFill(Side.Buy, new Qty(100m), new Price(25m), Money.Zero(Currency.USD));

        Assert.Equal(100m, state.PositionAt(id, 1500).Quantity);
    }

    [Fact]
    public void ApplyAllocationCommand_DoesNotResumePausedStrategyUnlessPauseIsExplicit()
    {
        using var state = new WorldState();
        var child = new StrategyId(1);

        state.ApplyAllocationCommand(new AllocationCommand
        {
            TargetStrategy = child,
            Pause = true,
            HasPause = true
        });

        state.ApplyAllocationCommand(new AllocationCommand
        {
            TargetStrategy = child,
            AllocationWeight = 0.25m,
            HasAllocationWeight = true
        });

        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var context = state.BuildContext(child, null, default, CreateCounters(), commands);

        Assert.True(context.IsPaused);
        Assert.Equal(0.25m, context.AllocationWeight);
    }

    [Fact]
    public void ApplyAllocationCommand_AllowsZeroAllocationWeight()
    {
        using var state = new WorldState();
        var child = new StrategyId(1);

        state.ApplyAllocationCommand(new AllocationCommand
        {
            TargetStrategy = child,
            AllocationWeight = 0m,
            HasAllocationWeight = true
        });

        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var context = state.BuildContext(child, null, default, CreateCounters(), commands);

        Assert.Equal(0m, context.AllocationWeight);
    }

    [Fact]
    public void BuildSnapshot_ReturnsOnlyActivePositionsFromReusableBuffer()
    {
        using var state = new WorldState();
        var id = new StrategyId(1);
        state.EnsureSnapshotCapacity(id, 4);

        state.PositionAt(id, 0).ApplyFill(Side.Buy, new Qty(10m), new Price(10m), Money.Zero(Currency.USD));
        state.PositionAt(id, 2).ApplyFill(Side.Buy, new Qty(5m), new Price(20m), Money.Zero(Currency.USD));

        var snapshot = state.BuildSnapshot(id, 4);
        var positions = snapshot.GetPositions();

        Assert.Equal(2, positions.Length);
        Assert.Equal(10m, positions[0].Quantity.Value);
        Assert.Equal(5m, positions[1].Quantity.Value);
        Assert.Equal(200m, snapshot.GrossExposure);

        state.PositionAt(id, 2).ApplyFill(Side.Sell, new Qty(5m), new Price(20m), Money.Zero(Currency.USD));

        snapshot = state.BuildSnapshot(id, 4);
        positions = snapshot.GetPositions();

        Assert.Equal(1, positions.Length);
        Assert.Equal(10m, positions[0].Quantity.Value);
        Assert.Equal(100m, snapshot.GrossExposure);
    }

    [Fact]
    public void BuildSnapshot_WarmedPathDoesNotAllocateManagedMemory()
    {
        using var state = new WorldState();
        var id = new StrategyId(1);
        state.EnsureSnapshotCapacity(id, 4);
        state.PositionAt(id, 0).ApplyFill(Side.Buy, new Qty(10m), new Price(10m), Money.Zero(Currency.USD));
        _ = state.BuildSnapshot(id, 4);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = state.BuildSnapshot(id, 4);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static int[] CreateCounters() => new int[PortfolioContext.CounterCount];
}
