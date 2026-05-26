using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform.Tests;

public class EngineLoopsTests
{
    [Fact]
    public void DispatchHierarchicalParallel_ProducesSameSnapshotsAsSequential()
    {
        using var sequentialRuntime = CreateRuntimeWithAssets(1);
        using var parallelRuntime = CreateRuntimeWithAssets(1);
        sequentialRuntime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        parallelRuntime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);

        var sequentialTree = CreateInitializedTree(sequentialRuntime, strategyCount: 12);
        var parallelTree = CreateInitializedTree(parallelRuntime, strategyCount: 12);

        var market = sequentialRuntime.CreateMarketKernel();
        var nodes = sequentialTree.GetNodesSnapshot();
        var contexts = new StrategyContext[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
            contexts[i] = new StrategyContext
            {
                Strategy = nodes[i].Strategy,
                Node = nodes[i].Node,
                ChildSnapshots = new PortfolioSnapshot[nodes[i].Node.ChildIds.Length],
                Counters = new int[PortfolioContext.CounterCount],
                OrderIntents = new OrderIntent[32]
            };

        using var parallelState = new ParallelDispatchState(parallelTree, threadCount: 4)
        {
            ParallelThreshold = 2
        };

        for (var tick = 0; tick < 3; tick++)
        {
            EngineLoops.DispatchHierarchical(in market, sequentialTree, sequentialRuntime.WorldState, contexts);
            EngineLoops.DispatchHierarchicalParallel(parallelRuntime, parallelTree, parallelState);
        }

        for (var i = 0; i < 12; i++)
        {
            var sequentialId = sequentialTree.GetNode(i).Node.Id;
            var parallelId = parallelTree.GetNode(i).Node.Id;
            var sequentialSnapshot = sequentialRuntime.WorldState.BuildSnapshot(sequentialId, sequentialRuntime.BatchMap.TotalSize, sequentialRuntime.CurrentTime);
            var parallelSnapshot = parallelRuntime.WorldState.BuildSnapshot(parallelId, parallelRuntime.BatchMap.TotalSize, parallelRuntime.CurrentTime);
            AssertEquivalentSnapshot(sequentialSnapshot, parallelSnapshot);
        }
    }

    [Fact]
    public void DispatchHierarchical_RunsOneHundredStrategiesAgainstSharedMarketKernel()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        var tree = CreateInitializedTree(runtime, strategyCount: 100);
        var contexts = CreateContexts(tree);
        var market = runtime.CreateMarketKernel();

        EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);

        Assert.Equal(100, tree.NodeCount);
        for (var i = 0; i < tree.NodeCount; i++)
        {
            var (strategy, _) = tree.GetNode(i);
            var drained = new OrderIntent[1];
            var count = runtime.WorldState.DrainOrderIntents(strategy.Id, drained);
            Assert.Equal(1, count);
            Assert.Equal(Side.Buy, drained[0].Side);
            Assert.Equal(new Qty(1m), drained[0].Quantity);
        }
    }

    [Fact]
    public void DispatchHierarchical_CommitsExecutionSpecsAsOrderIntents()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        var strategy = new OrderIntentStrategy();
        var tree = new StrategyTree();
        tree.Register(strategy, depth: 0);
        strategy.Initialize(runtime);
        var contexts = CreateContexts(tree);
        var market = runtime.CreateMarketKernel();

        EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);

        var drained = new OrderIntent[32];
        var count = runtime.WorldState.DrainOrderIntents(strategy.Id, drained);
        Assert.Equal(1, count);
        Assert.Equal(strategy.Id, drained[0].StrategyId);
        Assert.Equal(new AssetId(0), drained[0].AssetId);
        Assert.Equal(Side.Buy, drained[0].Side);
        Assert.Equal(new Qty(2m), drained[0].Quantity);
        Assert.Equal(OrderType.Limit, drained[0].Execution.OrderType);
        Assert.Equal(ExecutionLimitPriceMode.Bid, drained[0].Execution.LimitPriceMode);
        Assert.True(drained[0].Execution.PostOnly);
    }

    [Fact]
    public void DispatchHierarchical_AppliesParentCommandsAfterGroupPhase()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        var leaf = new AllocationAwareLeafStrategy();
        var group = new SnapshotCommandGroupStrategy();
        var tree = CreateHierarchy(runtime, leaf, group);
        ApplyTestFill(runtime, leaf.Id, new AssetId(0), Side.Buy, new Qty(1m), new Price(100m, Currency.USD));
        var contexts = CreateContexts(tree);
        var market = runtime.CreateMarketKernel();

        EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);

        Assert.True(group.SawChildSnapshot);
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var childContext = runtime.WorldState.BuildContext(leaf.Id, group.Id, default, CreateCounters(), commands);
        Assert.Equal(0.25m, childContext.AllocationWeight);
        Assert.True(childContext.IsPaused);

        EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);

        Assert.Equal(1m, runtime.WorldState.PositionAt(leaf.Id, 0).Quantity);
    }

    [Fact]
    public void DispatchHierarchical_OnGroupContext_AppliesFirstClassChildControls()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        var leaf = new AllocationAwareLeafStrategy();
        var group = new GeneratedGroupStrategy();
        var tree = CreateHierarchy(runtime, leaf, group);
        ApplyTestFill(runtime, leaf.Id, new AssetId(0), Side.Buy, new Qty(1m), new Price(100m, Currency.USD));
        var contexts = CreateContexts(tree);
        var market = runtime.CreateMarketKernel();

        EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);

        Assert.True(group.SawChildSnapshot);
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var childContext = runtime.WorldState.BuildContext(leaf.Id, group.Id, default, CreateCounters(), commands);
        Assert.Equal(0.5m, childContext.AllocationWeight);
        Assert.True(childContext.IsPaused);
    }

    [Fact]
    public void DispatchHierarchicalParallel_AppliesParentCommandsAfterGroupPhase()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        var leaf = new AllocationAwareLeafStrategy();
        var group = new SnapshotCommandGroupStrategy();
        var tree = CreateHierarchy(runtime, leaf, group);
        ApplyTestFill(runtime, leaf.Id, new AssetId(0), Side.Buy, new Qty(1m), new Price(100m, Currency.USD));

        using var state = new ParallelDispatchState(tree, threadCount: 2)
        {
            ParallelThreshold = 1
        };

        EngineLoops.DispatchHierarchicalParallel(runtime, tree, state);

        Assert.True(group.SawChildSnapshot);
        Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
        var childContext = runtime.WorldState.BuildContext(leaf.Id, group.Id, default, CreateCounters(), commands);
        Assert.Equal(0.25m, childContext.AllocationWeight);
        Assert.True(childContext.IsPaused);

        EngineLoops.DispatchHierarchicalParallel(runtime, tree, state);

        Assert.Equal(1m, runtime.WorldState.PositionAt(leaf.Id, 0).Quantity);
    }

    [Fact]
    public void DispatchHierarchicalParallel_UsesReusableWorkersOnlyAboveThreshold()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        var tree = CreateInitializedTree(runtime, strategyCount: 4);

        using var state = new ParallelDispatchState(tree, threadCount: 2)
        {
            ParallelThreshold = 8
        };

        EngineLoops.DispatchHierarchicalParallel(runtime, tree, state);
        Assert.Equal(0, state.LastQueuedWorkerCount);

        state.ParallelThreshold = 2;
        EngineLoops.DispatchHierarchicalParallel(runtime, tree, state);
        Assert.Equal(2, state.LastQueuedWorkerCount);
    }

    [Fact]
    public void StrategyEventProcessor_UsesConfiguredMaximumDegreeOfParallelism()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        var tree = CreateInitializedTree(runtime, strategyCount: 8);
        using var processor = new StrategyEventProcessor(runtime, tree)
        {
            UseParallelDispatch = true,
            ParallelThreshold = 1,
            MaxDegreeOfParallelism = 2
        };

        processor.Initialize();
        processor.ProcessProjectedEvent(new TestFinanceEvent(), StateTransitionResult.None);

        Assert.Equal(2, processor.LastQueuedParallelWorkerCount);
    }

    [Fact]
    public void DispatchHierarchicalParallel_PropagatesExecutionInvariantFailures()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        var tree = new StrategyTree();
        var strategy = new BuyOnceStrategy();
        tree.Register(strategy, depth: 0);
        strategy.Initialize(runtime);

        runtime.BatchMap.AddInstrument(new Instrument(new Asset("LATE", AssetClass.Equity), Venue.NASDAQ));
        runtime.Tensors.Grow();

        using var state = new ParallelDispatchState(tree, threadCount: 2)
        {
            ParallelThreshold = 1
        };

        var threw = false;
        try
        {
            EngineLoops.DispatchHierarchicalParallel(runtime, tree, state);
        }
        catch (UniverseTopologyChangedException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact]
    public void DispatchHierarchical_PropagatesExecutionInvariantFailures()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        var tree = new StrategyTree();
        var strategy = new BuyOnceStrategy();
        tree.Register(strategy, depth: 0);
        strategy.Initialize(runtime);

        runtime.BatchMap.AddInstrument(new Instrument(new Asset("LATE", AssetClass.Equity), Venue.NASDAQ));
        runtime.Tensors.Grow();

        var market = runtime.CreateMarketKernel();
        var contexts = CreateContexts(tree);

        var threw = false;
        try
        {
            EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);
        }
        catch (UniverseTopologyChangedException)
        {
            threw = true;
        }

        Assert.True(threw);
    }

    [Fact]
    public void DispatchHierarchical_PropagatesHotPathAllocationFailures()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        var tree = new StrategyTree();
        var strategy = new AllocatingStrategy();
        tree.Register(strategy, depth: 0);
        strategy.Initialize(runtime);

        var market = runtime.CreateMarketKernel();
        var contexts = CreateContexts(tree);

#if DEBUG
        HotPathAllocationException? ex = null;
        try
        {
            EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);
            EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);
        }
        catch (HotPathAllocationException caught)
        {
            ex = caught;
        }

        Assert.NotNull(ex);
#else
        EngineLoops.DispatchHierarchical(in market, tree, runtime.WorldState, contexts);
#endif
    }

    [Fact]
    public void DispatchHierarchicalParallel_PropagatesHotPathAllocationFailures()
    {
        using var runtime = CreateRuntimeWithAssets(1);
        var tree = new StrategyTree();
        var strategy = new AllocatingStrategy();
        tree.Register(strategy, depth: 0);
        strategy.Initialize(runtime);

        using var state = new ParallelDispatchState(tree, threadCount: 2)
        {
            ParallelThreshold = 1
        };

#if DEBUG
        HotPathAllocationException? ex = null;
        try
        {
            EngineLoops.DispatchHierarchicalParallel(runtime, tree, state);
            EngineLoops.DispatchHierarchicalParallel(runtime, tree, state);
        }
        catch (HotPathAllocationException caught)
        {
            ex = caught;
        }

        Assert.NotNull(ex);
#else
        EngineLoops.DispatchHierarchicalParallel(runtime, tree, state);
#endif
    }

    [Fact]
    public void StrategyTree_RegisterRejectsInvalidHierarchy()
    {
        var tree = new StrategyTree();
        var child = tree.Register(new BuyOnceStrategy(), depth: 0);

        Assert.Throws<InvalidOperationException>(() =>
            tree.Register(new BuyOnceStrategy(), depth: 1, children: [new StrategyId(999)]));

        Assert.Throws<InvalidOperationException>(() =>
            tree.Register(new BuyOnceStrategy(), depth: 1, children: [child, child]));

        Assert.Throws<InvalidOperationException>(() =>
            tree.Register(new BuyOnceStrategy(), depth: 0, children: [child]));

        tree.Register(new BuyOnceStrategy(), depth: 1, children: [child]);

        Assert.Throws<InvalidOperationException>(() =>
            tree.Register(new BuyOnceStrategy(), depth: 2, children: [child]));
    }

    private static RhodiumRuntime CreateRuntimeWithAssets(int count)
    {
        var runtime = new RhodiumRuntime();
        for (var i = 0; i < count; i++)
        {
            var instrument = new Instrument(new Asset($"ASSET{i}", AssetClass.Equity), Venue.NASDAQ);
            runtime.BatchMap.AddInstrument(instrument);
            runtime.SetContract(i, Contracts.Equity($"ASSET{i}", Venue.NASDAQ, Currency.USD));
            runtime.Tensors.Grow();
        }

        return runtime;
    }

    private static StrategyTree CreateInitializedTree(RhodiumRuntime runtime, int strategyCount)
    {
        var tree = new StrategyTree();
        for (var i = 0; i < strategyCount; i++)
        {
            var strategy = new BuyOnceStrategy();
            tree.Register(strategy, depth: 0);
            strategy.Initialize(runtime);
        }

        return tree;
    }

    private static StrategyTree CreateHierarchy(
        RhodiumRuntime runtime,
        AllocationAwareLeafStrategy leaf,
        Strategy group)
    {
        var tree = new StrategyTree();
        var leafId = tree.Register(leaf, depth: 0);
        tree.Register(group, depth: 1, children: [leafId]);

        for (var i = 0; i < tree.NodeCount; i++)
        {
            var (strategy, _) = tree.GetNode(i);
            strategy.Initialize(runtime);
        }

        return tree;
    }

    private static StrategyContext[] CreateContexts(StrategyTree tree)
    {
        var nodes = tree.GetNodesSnapshot();
        var contexts = new StrategyContext[nodes.Length];
        for (var i = 0; i < nodes.Length; i++)
        {
            contexts[i] = new StrategyContext
            {
                Strategy = nodes[i].Strategy,
                Node = nodes[i].Node,
                ChildSnapshots = new PortfolioSnapshot[nodes[i].Node.ChildIds.Length],
                Counters = new int[PortfolioContext.CounterCount],
                OrderIntents = new OrderIntent[32]
            };
        }
        return contexts;
    }

    private static void AssertEquivalentSnapshot(PortfolioSnapshot expected, PortfolioSnapshot actual)
    {
        Assert.Equal(expected.NetLiquidation, actual.NetLiquidation);
        Assert.Equal(expected.UnrealizedPnL, actual.UnrealizedPnL);
        Assert.Equal(expected.RealizedPnL, actual.RealizedPnL);
        Assert.Equal(expected.GrossExposure, actual.GrossExposure);
        Assert.Equal(expected.NetExposure, actual.NetExposure);
        Assert.Equal(expected.RollingStats.SharpeRatio, actual.RollingStats.SharpeRatio);
        Assert.Equal(expected.RollingStats.Volatility, actual.RollingStats.Volatility);

        var expectedPositions = expected.GetPositions();
        var actualPositions = actual.GetPositions();
        Assert.Equal(expectedPositions.Length, actualPositions.Length);
        for (var i = 0; i < expectedPositions.Length; i++)
        {
            Assert.Equal(expectedPositions[i].Instrument, actualPositions[i].Instrument);
            Assert.Equal(expectedPositions[i].Quantity, actualPositions[i].Quantity);
            Assert.Equal(expectedPositions[i].AvgEntryPrice, actualPositions[i].AvgEntryPrice);
            Assert.Equal(expectedPositions[i].RealizedPnL, actualPositions[i].RealizedPnL);
        }
    }

    private static int[] CreateCounters() => new int[PortfolioContext.CounterCount];

    private static void ApplyTestFill(
        RhodiumRuntime runtime,
        StrategyId strategyId,
        AssetId assetId,
        Side side,
        Qty quantity,
        Price price)
    {
        var contract = runtime.CreateMarketKernel().GetContract(assetId);
        runtime.WorldState.PositionAt(strategyId, assetId.VirtualIndex)
            .ApplyFill(contract, side, quantity, price, Money.Zero(price.Currency));
    }

    private sealed class BuyOnceStrategy : Strategy
    {
        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
            => portfolio.Buy(new AssetId(0), new Qty(1m), Execution.Market());
    }

    private sealed record TestFinanceEvent : FinanceEvent;

    private sealed class OrderIntentStrategy : Strategy
    {
        protected override void OnInitialize(in SetupContext setup)
            => setup.AddEquity("ASSET0");

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
            => portfolio.Buy(new AssetId(0), new Qty(2m), Execution.Limit().AtBid().WithPostOnly());
    }

    private sealed class AllocationAwareLeafStrategy : Strategy
    {
        protected override void OnInitialize(in SetupContext setup)
            => setup.AddEquity("ASSET0");

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
            => portfolio.Buy(new AssetId(0), new Qty(portfolio.AllocationWeight), Execution.Market());
    }

    private sealed class SnapshotCommandGroupStrategy : Strategy
    {
        public bool SawChildSnapshot { get; private set; }

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
            var snapshots = portfolio.GetChildSnapshots();
            SawChildSnapshot |= snapshots.Length == 1 && snapshots[0].GrossExposure > 0m;

            var child = portfolio.ChildIds[0];
            portfolio.SetChildAllocation(child, 0.25m);
            portfolio.PauseChild(child);
        }
    }

    private sealed class GeneratedGroupStrategy : Strategy
    {
        public bool SawChildSnapshot { get; private set; }

        protected override void OnGroup(ref GroupContext group)
        {
            SawChildSnapshot |= group.Children.Length == 1 && group.Child(0).GrossExposure > 0m;

            var child = group.Child(0);
            group.SetAllocation(child.StrategyId, 0.5m);
            group.Pause(child.StrategyId);
        }
    }

    private sealed class AllocatingStrategy : Strategy
    {
        private static object? s_sink;

        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
            s_sink = new object();
        }
    }
}
