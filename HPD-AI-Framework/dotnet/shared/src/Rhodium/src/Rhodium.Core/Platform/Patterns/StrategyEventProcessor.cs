using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform.Patterns;

internal delegate void OrderIntentSink(in OrderIntent intent, in MarketKernel market);
internal delegate void ProcessedMarketEventSink(FinanceEvent evt, in MarketKernel market);

internal sealed class StrategyEventProcessor : IDisposable
{
    private readonly RhodiumRuntime _runtime;
    private readonly StrategyTree _tree;
    private readonly OrderIntentSink? _orderIntentSink;
    private readonly ProcessedMarketEventSink? _processedMarketEventSink;
    private readonly OrderIntent[] _orderIntentSubmitBuffer;
    private StrategyContext[] _dispatchContexts = [];
    private ParallelDispatchState? _parallelDispatchState;

    public StrategyEventProcessor(
        RhodiumRuntime runtime,
        StrategyTree tree,
        OrderIntentSink? orderIntentSink = null,
        ProcessedMarketEventSink? processedMarketEventSink = null,
        int orderIntentBufferSize = 32)
    {
        _runtime = runtime;
        _tree = tree;
        _orderIntentSink = orderIntentSink;
        _processedMarketEventSink = processedMarketEventSink;
        _orderIntentSubmitBuffer = new OrderIntent[orderIntentBufferSize];
    }

    public bool UseParallelDispatch { get; set; }
    public int ParallelThreshold { get; set; } = 128;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    internal int LastQueuedParallelWorkerCount => _parallelDispatchState?.LastQueuedWorkerCount ?? 0;

    public void Initialize()
    {
        for (var depth = 0; depth <= _tree.MaxDepth; depth++)
        {
            for (var i = 0; i < _tree.NodeCount; i++)
            {
                var (strategy, node) = _tree.GetNode(i);
                if (node.Depth == depth)
                    strategy.Initialize(_runtime);
            }
        }

        BuildDispatchContexts();
        if (UseParallelDispatch)
        {
            _parallelDispatchState?.Dispose();
            _parallelDispatchState = new ParallelDispatchState(_tree, MaxDegreeOfParallelism)
            {
                ParallelThreshold = ParallelThreshold
            };
        }
    }

    internal void ProcessProjectedEvent(FinanceEvent evt, in StateTransitionResult transition)
    {
        var market = _runtime.CreateMarketKernel();
        if (transition.RequiresAdjustment)
            market.RunAdjustmentKernel();

        _processedMarketEventSink?.Invoke(evt, in market);

        if (evt is ExecutionEvent execution)
        {
            DispatchExecution(in market, execution, in transition);
            if (transition.PositionTransition.Kind != PositionTransitionKind.None)
                EngineLoops.DispatchGroupOnlyHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is LifecycleEvent lifecycle)
        {
            DispatchLifecycle(in market, lifecycle);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is BarClosed)
        {
            if (UseParallelDispatch && _parallelDispatchState is not null)
                EngineLoops.DispatchBarsHierarchicalParallel(_runtime, _tree, _parallelDispatchState);
            else
                EngineLoops.DispatchBarsHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is QuoteReceived quote)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(quote.Instrument);
            EngineLoops.DispatchQuotesHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts, in quote, start, length);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is TradeOccurred trade)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(trade.Instrument);
            EngineLoops.DispatchTradesHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts, in trade, start, length);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is BookSnapshotReceived book)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(book.Instrument);
            EngineLoops.DispatchBooksHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts, in book, start, length);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is BookLevelDeltaReceived bookDelta)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(bookDelta.Instrument);
            EngineLoops.DispatchBookLevelDeltasHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts, in bookDelta, start, length);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is BookLevelDeltasReceived bookDeltas)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(bookDeltas.Instrument);
            EngineLoops.DispatchBookLevelDeltasHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts, in bookDeltas, start, length);
            SubmitOrderIntents(in market);
            return;
        }

        if (UseParallelDispatch && _parallelDispatchState is not null)
            EngineLoops.DispatchHierarchicalParallel(_runtime, _tree, _parallelDispatchState);
        else
            EngineLoops.DispatchHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts);

        SubmitOrderIntents(in market);
    }

    private void DispatchExecution(in MarketKernel market, ExecutionEvent execution, in StateTransitionResult transition)
    {
        var strategyId = execution switch
        {
            OrderAccepted e => e.StrategyId,
            OrderModified e => e.StrategyId,
            OrderRejected e => e.StrategyId,
            OrderCancelled e => e.StrategyId,
            OrderExpired e => e.StrategyId,
            OrderFilled e => e.StrategyId,
            PackageLegFilled e => e.StrategyId,
            _ => default
        };

        for (var i = 0; i < _dispatchContexts.Length; i++)
        {
            ref var context = ref _dispatchContexts[i];
            if (context.Node.Id != strategyId)
                continue;

            Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
            var counters = context.Counters.AsSpan();
            counters.Clear();
            var orderIntents = context.OrderIntents.AsSpan();
            orderIntents.Clear();
            var portfolio = _runtime.WorldState.BuildContext(
                context.Node.Id,
                context.Node.ParentId,
                context.Node.ChildIds.Span,
                counters,
                commands,
                orderIntents: orderIntents);

            context.Strategy.RunExecutionGuarded(in market, ref portfolio, execution, in transition);
            _runtime.WorldState.CommitContext(context.Node.Id, ref portfolio);
            break;
        }
    }

    private void DispatchLifecycle(in MarketKernel market, LifecycleEvent lifecycle)
    {
        for (var i = 0; i < _dispatchContexts.Length; i++)
        {
            ref var context = ref _dispatchContexts[i];
            if (lifecycle is Scheduled scheduled
                && scheduled.StrategyId.HasValue
                && context.Node.Id != scheduled.StrategyId.Value)
            {
                continue;
            }

            Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
            var counters = context.Counters.AsSpan();
            counters.Clear();
            var orderIntents = context.OrderIntents.AsSpan();
            orderIntents.Clear();
            var childSnapshots = BuildChildSnapshots(context, market.UniverseSize);
            var portfolio = _runtime.WorldState.BuildContext(
                context.Node.Id,
                context.Node.ParentId,
                context.Node.ChildIds.Span,
                counters,
                commands,
                childSnapshots,
                orderIntents);

            context.Strategy.RunLifecycleGuarded(in market, ref portfolio, lifecycle);
            _runtime.WorldState.CommitContext(context.Node.Id, ref portfolio);
        }
    }

    private ReadOnlySpan<PortfolioSnapshot> BuildChildSnapshots(in StrategyContext context, int universeSize)
    {
        if (context.Node.ChildIds.IsEmpty) return default;
        if (context.ChildSnapshots.Length < context.Node.ChildIds.Length)
            throw new InvalidOperationException("Strategy context child snapshot buffer is smaller than its child set.");

        for (var i = 0; i < context.Node.ChildIds.Length; i++)
            context.ChildSnapshots[i] = _runtime.WorldState.BuildSnapshot(context.Node.ChildIds.Span[i], universeSize, _runtime.CurrentTime);

        return context.ChildSnapshots.AsSpan(0, context.Node.ChildIds.Length);
    }

    private void SubmitOrderIntents(in MarketKernel market)
    {
        var sink = _orderIntentSink;
        if (sink is null)
            return;

        var intents = _orderIntentSubmitBuffer.AsSpan();
        for (var i = 0; i < _tree.NodeCount; i++)
        {
            var (strategy, _) = _tree.GetNode(i);
            var count = _runtime.WorldState.DrainOrderIntents(strategy.Id, intents);
            for (var intentIndex = 0; intentIndex < count; intentIndex++)
                sink(in intents[intentIndex], in market);
        }
    }

    private void BuildDispatchContexts()
    {
        var nodeCount = _tree.NodeCount;
        _dispatchContexts = new StrategyContext[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            var (strategy, node) = _tree.GetNode(i);
            _dispatchContexts[i] = new StrategyContext
            {
                Strategy = strategy,
                Node = node,
                ChildSnapshots = new PortfolioSnapshot[node.ChildIds.Length],
                Counters = new int[PortfolioContext.CounterCount],
                OrderIntents = new OrderIntent[32]
            };
        }
    }

    public void Dispose()
    {
        _parallelDispatchState?.Dispose();
    }
}
