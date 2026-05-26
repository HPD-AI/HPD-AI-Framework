using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Control.Tests;

public class WorldStateTests
{
    private static readonly InstrumentContract EquityContract = Contracts.Equity("TEST", Venue.NASDAQ, Currency.USD);

    [Fact]
    public void WorldState_PositionAt_IsPerStrategy()
    {
        using var state = new WorldState();
        var a = new StrategyId(1);
        var b = new StrategyId(2);

        state.PositionAt(a, 10).ApplyFill(EquityContract, Side.Buy, new Qty(50m), new Price(100m), Money.Zero(Currency.USD));
        state.PositionAt(b, 10).ApplyFill(EquityContract, Side.Buy, new Qty(100m), new Price(100m), Money.Zero(Currency.USD));

        Assert.Equal(50m, state.PositionAt(a, 10).Quantity);
        Assert.Equal(100m, state.PositionAt(b, 10).Quantity);
    }

    [Fact]
    public void WorldState_MultiplePages_AreAllocatedOnDemand()
    {
        using var state = new WorldState();
        var id = new StrategyId(1);

        state.PositionAt(id, 1500).ApplyFill(EquityContract, Side.Buy, new Qty(100m), new Price(25m), Money.Zero(Currency.USD));

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

        state.PositionAt(id, 0).ApplyFill(EquityContract, Side.Buy, new Qty(10m), new Price(10m), Money.Zero(Currency.USD));
        state.PositionAt(id, 2).ApplyFill(EquityContract, Side.Buy, new Qty(5m), new Price(20m), Money.Zero(Currency.USD));

        var snapshotTime = Instant.FromUnixSeconds(123);
        var snapshot = state.BuildSnapshot(id, 4, snapshotTime);
        var positions = snapshot.GetPositions();

        Assert.Equal(2, positions.Length);
        Assert.Equal(10m, positions[0].Quantity.Value);
        Assert.Equal(5m, positions[1].Quantity.Value);
        Assert.Equal(snapshotTime, positions[0].OpenedAt);
        Assert.Equal(snapshotTime, positions[1].OpenedAt);
        Assert.Equal(200m, snapshot.GrossExposure);

        state.PositionAt(id, 2).ApplyFill(EquityContract, Side.Sell, new Qty(5m), new Price(20m), Money.Zero(Currency.USD));

        snapshot = state.BuildSnapshot(id, 4, Instant.FromUnixSeconds(456));
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
        state.PositionAt(id, 0).ApplyFill(EquityContract, Side.Buy, new Qty(10m), new Price(10m), Money.Zero(Currency.USD));
        var snapshotTime = Instant.FromUnixSeconds(1);
        _ = state.BuildSnapshot(id, 4, snapshotTime);

        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = state.BuildSnapshot(id, 4, snapshotTime);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WorldState_ParallelContexts_AreIsolatedByStrategySlice()
    {
        using var state = new WorldState();
        var first = new StrategyId(1);
        var second = new StrategyId(2);

        Parallel.Invoke(
            () => RunStrategySlice(state, first, new AssetId(0), 0.25m, 10m),
            () => RunStrategySlice(state, second, new AssetId(1), 0.75m, 20m));

        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var intents = new OrderIntent[4];
        var firstContext = state.BuildContext(first, null, default, CreateCounters(), commands, orderIntents: intents);
        Assert.Equal(0.25m, firstContext.AllocationWeight);
        var firstCount = state.DrainOrderIntents(first, intents);
        Assert.Equal(1, firstCount);
        Assert.Equal(first, intents[0].StrategyId);
        Assert.Equal(new AssetId(0), intents[0].AssetId);

        var secondContext = state.BuildContext(second, null, default, CreateCounters(), commands, orderIntents: intents);
        Assert.Equal(0.75m, secondContext.AllocationWeight);
        var secondCount = state.DrainOrderIntents(second, intents);
        Assert.Equal(1, secondCount);
        Assert.Equal(second, intents[0].StrategyId);
        Assert.Equal(new AssetId(1), intents[0].AssetId);
    }

    private static int[] CreateCounters() => new int[PortfolioContext.CounterCount];

    private static void RunStrategySlice(
        WorldState state,
        StrategyId strategyId,
        AssetId assetId,
        decimal allocationWeight,
        decimal quantity)
    {
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var intents = new OrderIntent[4];
        var portfolio = state.BuildContext(strategyId, null, default, CreateCounters(), commands, orderIntents: intents);
        portfolio.AllocationWeight = allocationWeight;
        portfolio.Buy(assetId, new Qty(quantity), ExecutionSpec.Market);
        state.CommitContext(strategyId, ref portfolio);
    }
}
