using BenchmarkDotNet.Attributes;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class DispatchBenchmarks
{
    private RhodiumRuntime _singleRuntime = null!;
    private StrategyTree _singleTree = null!;
    private StrategyContext[] _singleContexts = [];

    private RhodiumRuntime _sequentialRuntime = null!;
    private StrategyTree _sequentialTree = null!;
    private StrategyContext[] _sequentialContexts = [];

    private RhodiumRuntime _parallelRuntime = null!;
    private StrategyTree _parallelTree = null!;
    private ParallelDispatchState _parallelState = null!;

    [GlobalSetup]
    public void Setup()
    {
        (_singleRuntime, _singleTree, _singleContexts) = CreateScenario(strategyCount: 1);
        (_sequentialRuntime, _sequentialTree, _sequentialContexts) = CreateScenario(strategyCount: 100);

        (_parallelRuntime, _parallelTree, _) = CreateScenario(strategyCount: 100);
        _parallelState = new ParallelDispatchState(_parallelTree, threadCount: 4);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _parallelState.Dispose();
        _singleRuntime.Dispose();
        _sequentialRuntime.Dispose();
        _parallelRuntime.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void SingleStrategySequential()
    {
        var market = _singleRuntime.CreateMarketKernel();
        EngineLoops.DispatchHierarchical(in market, _singleTree, _singleRuntime.WorldState, _singleContexts);
    }

    [Benchmark]
    public void HundredStrategiesSequential()
    {
        var market = _sequentialRuntime.CreateMarketKernel();
        EngineLoops.DispatchHierarchical(in market, _sequentialTree, _sequentialRuntime.WorldState, _sequentialContexts);
    }

    [Benchmark]
    public void HundredStrategiesParallel()
        => EngineLoops.DispatchHierarchicalParallel(_parallelRuntime, _parallelTree, _parallelState);

    private static (RhodiumRuntime Runtime, StrategyTree Tree, StrategyContext[] Contexts) CreateScenario(int strategyCount)
    {
        var runtime = new RhodiumRuntime();
        runtime.BatchMap.AddInstrument(new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ));
        runtime.Tensors.Grow();
        runtime.Tensors.GetScalar(Field.Close, 0) = new PriceF64(100);
        runtime.SetMetadata(0, SecurityMetadata.Equity(
            new Instrument(new Asset("SPY", AssetClass.Equity), Venue.NASDAQ),
            tickSize: 0.01m));

        var tree = new StrategyTree();
        for (var i = 0; i < strategyCount; i++)
        {
            var strategy = new BenchmarkStrategy();
            tree.Register(strategy, depth: 0);
            strategy.Initialize(runtime);
        }

        var contexts = CreateContexts(tree);
        return (runtime, tree, contexts);
    }

    private static StrategyContext[] CreateContexts(StrategyTree tree)
    {
        var nodes = tree.Nodes;
        var contexts = new StrategyContext[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
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

    private sealed class BenchmarkStrategy : Strategy
    {
        protected override void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio)
        {
            if (market.GetScalar(Field.Close, new AssetId(0)) > 0)
                portfolio.Buy(new AssetId(0), new Qty(1m), in market);
        }
    }
}
