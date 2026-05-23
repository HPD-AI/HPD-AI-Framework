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
        foreach (var (strategy, _) in _tree.Nodes.OrderBy(static n => n.Node.Depth))
            strategy.Initialize(_runtime);

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

    public void ProcessEvent(FinanceEvent evt)
    {
        var transition = StateTransitions.Apply(_runtime.WorldState, _runtime.Tensors, _runtime.BatchMap, evt);
        UpdateMarketDepth(evt);

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

        if (evt is BookUpdated book)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(book.Instrument);
            EngineLoops.DispatchBooksHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts, in book, start, length);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is BookDeltaReceived bookDelta)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(bookDelta.Instrument);
            EngineLoops.DispatchBookDeltasHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts, in bookDelta, start, length);
            SubmitOrderIntents(in market);
            return;
        }

        if (evt is BookDeltasReceived bookDeltas)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(bookDeltas.Instrument);
            EngineLoops.DispatchBookDeltasHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts, in bookDeltas, start, length);
            SubmitOrderIntents(in market);
            return;
        }

        if (UseParallelDispatch && _parallelDispatchState is not null)
            EngineLoops.DispatchHierarchicalParallel(_runtime, _tree, _parallelDispatchState);
        else
            EngineLoops.DispatchHierarchical(in market, _tree, _runtime.WorldState, _dispatchContexts);

        SubmitOrderIntents(in market);
    }

    private void UpdateMarketDepth(FinanceEvent evt)
    {
        if (evt is QuoteReceived quote)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(quote.Instrument);
            for (var i = 0; i < length; i++)
            {
                var virtualIndex = start + i;
                _runtime.UpdateDepthLevel(virtualIndex, quote.Instrument, Side.Buy, quote.Quote.Bid, quote.Quote.BidSize, quote.Quote.Time.ExchangeTime);
                _runtime.UpdateDepthLevel(virtualIndex, quote.Instrument, Side.Sell, quote.Quote.Ask, quote.Quote.AskSize, quote.Quote.Time.ExchangeTime);
            }
        }
        else if (evt is BookUpdated book)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(book.Instrument);
            for (var i = 0; i < length; i++)
            {
                var virtualIndex = start + i;
                _runtime.ClearDepth(virtualIndex, book.Instrument);

                foreach (var level in book.Book.Bids)
                    _runtime.UpdateDepthLevel(virtualIndex, book.Instrument, Side.Buy, level.Price, level.Size, book.Book.Time);

                foreach (var level in book.Book.Asks)
                    _runtime.UpdateDepthLevel(virtualIndex, book.Instrument, Side.Sell, level.Price, level.Size, book.Book.Time);
            }
        }
        else if (evt is BookDeltaReceived delta)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(delta.Instrument);
            for (var i = 0; i < length; i++)
                ApplyBookDelta(start + i, delta.Instrument, delta.Delta, delta.Time);
        }
        else if (evt is BookDeltasReceived deltas)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(deltas.Instrument);
            for (var i = 0; i < length; i++)
            {
                var virtualIndex = start + i;
                foreach (var bookDelta in deltas.Deltas)
                    ApplyBookDelta(virtualIndex, deltas.Instrument, bookDelta, deltas.Time);
            }
        }
        else if (evt is BookDepthSnapshotReceived snapshot)
        {
            var (start, length) = _runtime.BatchMap.GetInstrumentRange(snapshot.Instrument);
            for (var i = 0; i < length; i++)
            {
                var virtualIndex = start + i;
                _runtime.ClearDepth(virtualIndex, snapshot.Instrument);

                foreach (var level in snapshot.Bids.Take(snapshot.Depth))
                    _runtime.UpdateDepthLevel(virtualIndex, snapshot.Instrument, Side.Buy, level.Price, level.Size, snapshot.Time);

                foreach (var level in snapshot.Asks.Take(snapshot.Depth))
                    _runtime.UpdateDepthLevel(virtualIndex, snapshot.Instrument, Side.Sell, level.Price, level.Size, snapshot.Time);
            }
        }
    }

    private void ApplyBookDelta(int virtualIndex, Instrument instrument, BookDelta delta, Instant time)
    {
        if (delta.Action == BookAction.Clear)
        {
            _runtime.ClearDepth(virtualIndex, instrument);
            return;
        }

        var quantity = delta.Action == BookAction.Delete
            ? Qty.Zero
            : delta.Size;
        _runtime.UpdateDepthLevel(virtualIndex, instrument, delta.Side, delta.Price, quantity, time);
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
            context.ChildSnapshots[i] = _runtime.WorldState.BuildSnapshot(context.Node.ChildIds.Span[i], universeSize);

        return context.ChildSnapshots.AsSpan(0, context.Node.ChildIds.Length);
    }

    private void SubmitOrderIntents(in MarketKernel market)
    {
        var sink = _orderIntentSink;
        if (sink is null)
            return;

        var intents = _orderIntentSubmitBuffer.AsSpan();
        foreach (var (strategy, _) in _tree.Nodes)
        {
            var count = _runtime.WorldState.DrainOrderIntents(strategy.Id, intents);
            for (var i = 0; i < count; i++)
                sink(in intents[i], in market);
        }
    }

    private void BuildDispatchContexts()
    {
        var nodes = _tree.Nodes;
        _dispatchContexts = new StrategyContext[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            _dispatchContexts[i] = new StrategyContext
            {
                Strategy = nodes[i].Strategy,
                Node = nodes[i].Node,
                ChildSnapshots = new PortfolioSnapshot[nodes[i].Node.ChildIds.Length],
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
