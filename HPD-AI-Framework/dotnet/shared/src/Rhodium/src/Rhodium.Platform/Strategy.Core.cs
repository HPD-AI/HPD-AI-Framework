using System.ComponentModel;
using System.Runtime.CompilerServices;
using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Platform;

/// <summary>
/// Official generated strategy authoring surface.
/// </summary>
public abstract partial class Strategy
{
#if DEBUG
    private static readonly object MarketGuardWarmupLock = new();
    private static readonly HashSet<Type> MarketGuardWarmedTypes = [];
    private static readonly object ExecutionGuardWarmupLock = new();
    private static readonly HashSet<Type> ExecutionGuardWarmedTypes = [];
    private static readonly object LifecycleGuardWarmupLock = new();
    private static readonly HashSet<Type> LifecycleGuardWarmedTypes = [];
#endif

    private RhodiumRuntime? _registrationRuntime;
    private readonly List<AssetId> _registeredAssetBuilder = [];
    private AssetId[] _registeredAssets = [];
    private int _initializedVersion;

    public StrategyId Id { get; internal set; }
    public int Depth { get; internal set; }

    internal void Initialize(RhodiumRuntime runtime)
    {
        _registeredAssetBuilder.Clear();
        _registeredAssets = [];
        _registrationRuntime = runtime;
        var market = runtime.CreateMarketKernel();
        var setup = new SetupContext(this, runtime, in market);
        OnInitialize(in setup);
        _registeredAssets = _registeredAssetBuilder.ToArray();
        __GeneratedInitialize(in market);
        _initializedVersion = runtime.BatchMap.Version;
        runtime.WorldState.EnsureSnapshotCapacity(Id, runtime.BatchMap.TotalSize);
        _registrationRuntime = null;
    }

    protected virtual void OnInitialize(in SetupContext setup) { }

    internal virtual void OnTickCore(in MarketKernel market, ref PortfolioContext portfolio)
    {
        __GeneratedRunTick(in market, ref portfolio);
        RunGroupHook(in market, ref portfolio);
    }

    public virtual void OnError(Exception ex) { }

    protected ReadOnlySpan<AssetId> RegisteredAssets => _registeredAssets;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal AssetId AddEquityForSetup(string symbol) => AddEquityForSetup(symbol, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal AssetId AddEquityForSetup(string symbol, int variantOffset)
    {
        var runtime = _registrationRuntime
            ?? throw new InvalidOperationException("Instruments can only be added during OnInitialize.");

        var instrument = new Instrument(new Asset(symbol, AssetClass.Equity), Venue.NASDAQ);

        try
        {
            var existing = runtime.BatchMap.GetInstrumentRange(instrument);
            return TrackRegisteredAssetForSetup(new AssetId(existing.Start + variantOffset));
        }
        catch (KeyNotFoundException)
        {
            var variants = Math.Max(variantOffset + 1, 10);
            runtime.BatchMap.AddInstrument(instrument, variants);
            for (var i = 0; i < variants; i++)
                runtime.Tensors.Grow();

            var created = runtime.BatchMap.GetInstrumentRange(instrument);
            return TrackRegisteredAssetForSetup(new AssetId(created.Start + variantOffset));
        }
    }

    internal AssetId TrackRegisteredAssetForSetup(AssetId id)
    {
        if (!_registeredAssetBuilder.Contains(id))
            _registeredAssetBuilder.Add(id);

        return id;
    }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected void __GeneratedRegisterIndicator<T>(VectorField<T> field) where T : unmanaged
    {
        var runtime = _registrationRuntime
            ?? throw new InvalidOperationException("Tensor fields can only be registered during initialization.");

        _ = runtime.Tensors.GetScalar(field, 0);
    }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected void __GeneratedRegisterPortfolioField<T>(VectorField<T> field) where T : unmanaged
    {
        var runtime = _registrationRuntime
            ?? throw new InvalidOperationException("Portfolio tensor fields can only be registered during initialization.");

        runtime.WorldState.RegisterStrategyField(Id, field, runtime.BatchMap.TotalSize);
    }

    internal void RunTickGuarded(in MarketKernel market, ref PortfolioContext portfolio)
        => RunMarketGuarded(in market, ref portfolio, static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio) =>
            strategy.OnTickCore(in market, ref portfolio));

    internal void RunGeneratedTickGuarded(in MarketKernel market, ref PortfolioContext portfolio)
        => RunMarketGuarded(in market, ref portfolio, static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio) =>
            strategy.__GeneratedRunTick(in market, ref portfolio));

    internal void RunQuoteGuarded(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        in QuoteReceived evt,
        int assetRangeStart,
        int assetRangeLength)
        => RunMarketGuarded(
            in market,
            ref portfolio,
            new QuoteDispatch(evt, assetRangeStart, assetRangeLength),
            static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio, in QuoteDispatch dispatch) =>
                strategy.__GeneratedRunQuote(in market, ref portfolio, dispatch.Event, dispatch.AssetRangeStart, dispatch.AssetRangeLength));

    internal void RunTradeGuarded(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        in TradeOccurred evt,
        int assetRangeStart,
        int assetRangeLength)
        => RunMarketGuarded(
            in market,
            ref portfolio,
            new TradeDispatch(evt, assetRangeStart, assetRangeLength),
            static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio, in TradeDispatch dispatch) =>
                strategy.__GeneratedRunTrade(in market, ref portfolio, dispatch.Event, dispatch.AssetRangeStart, dispatch.AssetRangeLength));

    internal void RunBookGuarded(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        in BookUpdated evt,
        int assetRangeStart,
        int assetRangeLength)
        => RunMarketGuarded(
            in market,
            ref portfolio,
            new BookDispatch(evt, assetRangeStart, assetRangeLength),
            static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio, in BookDispatch dispatch) =>
                strategy.__GeneratedRunBook(in market, ref portfolio, dispatch.Event, dispatch.AssetRangeStart, dispatch.AssetRangeLength));

    internal void RunBookDeltaGuarded(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        in BookDeltaReceived evt,
        int assetRangeStart,
        int assetRangeLength)
        => RunMarketGuarded(
            in market,
            ref portfolio,
            new BookDeltaDispatch(evt, assetRangeStart, assetRangeLength),
            static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio, in BookDeltaDispatch dispatch) =>
                strategy.__GeneratedRunBookDelta(in market, ref portfolio, dispatch.Event, dispatch.AssetRangeStart, dispatch.AssetRangeLength));

    internal void RunBookDeltasGuarded(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        in BookDeltasReceived evt,
        int assetRangeStart,
        int assetRangeLength)
        => RunMarketGuarded(
            in market,
            ref portfolio,
            new BookDeltasDispatch(evt, assetRangeStart, assetRangeLength),
            static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio, in BookDeltasDispatch dispatch) =>
                strategy.__GeneratedRunBookDeltas(in market, ref portfolio, dispatch.Event, dispatch.AssetRangeStart, dispatch.AssetRangeLength));

    internal void RunGroupGuarded(in MarketKernel market, ref PortfolioContext portfolio)
        => RunMarketGuarded(in market, ref portfolio, static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio) =>
            strategy.RunGroupHook(in market, ref portfolio));

    internal void RunBarGuarded(in MarketKernel market, ref PortfolioContext portfolio)
        => RunMarketGuarded(in market, ref portfolio, static (Strategy strategy, in MarketKernel market, ref PortfolioContext portfolio) =>
        {
            strategy.__GeneratedRunBars(in market, ref portfolio);
            strategy.RunGroupHook(in market, ref portfolio);
        });

    private void RunMarketGuarded(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        MarketDispatchInvoker invoker)
    {
        if (market.UniverseVersion != _initializedVersion)
        {
            throw new UniverseTopologyChangedException(_initializedVersion, market.UniverseVersion);
        }

#if DEBUG
        var guardWarmed = IsMarketGuardWarmed(GetType());
        long start = guardWarmed ? GC.GetAllocatedBytesForCurrentThread() : 0;
#endif

        invoker(this, in market, ref portfolio);

#if DEBUG
        if (guardWarmed)
        {
            long diff = GC.GetAllocatedBytesForCurrentThread() - start;
            if (diff > 0)
                throw new HotPathAllocationException(diff);
        }
        else
        {
            MarkMarketGuardWarmed(GetType());
        }
#endif
    }

    private delegate void MarketDispatchInvoker(
        Strategy strategy,
        in MarketKernel market,
        ref PortfolioContext portfolio);

    private void RunMarketGuarded<TDispatch>(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        in TDispatch dispatch,
        MarketDispatchInvoker<TDispatch> invoker)
        where TDispatch : struct
    {
        if (market.UniverseVersion != _initializedVersion)
        {
            throw new UniverseTopologyChangedException(_initializedVersion, market.UniverseVersion);
        }

#if DEBUG
        var guardWarmed = IsMarketGuardWarmed(GetType());
        long start = guardWarmed ? GC.GetAllocatedBytesForCurrentThread() : 0;
#endif

        invoker(this, in market, ref portfolio, in dispatch);

#if DEBUG
        if (guardWarmed)
        {
            long diff = GC.GetAllocatedBytesForCurrentThread() - start;
            if (diff > 0)
                throw new HotPathAllocationException(diff);
        }
        else
        {
            MarkMarketGuardWarmed(GetType());
        }
#endif
    }

    private delegate void MarketDispatchInvoker<TDispatch>(
        Strategy strategy,
        in MarketKernel market,
        ref PortfolioContext portfolio,
        in TDispatch dispatch)
        where TDispatch : struct;

    private readonly struct QuoteDispatch
    {
        public QuoteDispatch(QuoteReceived @event, int assetRangeStart, int assetRangeLength)
        {
            Event = @event;
            AssetRangeStart = assetRangeStart;
            AssetRangeLength = assetRangeLength;
        }

        public QuoteReceived Event { get; }
        public int AssetRangeStart { get; }
        public int AssetRangeLength { get; }
    }

    private readonly struct TradeDispatch
    {
        public TradeDispatch(TradeOccurred @event, int assetRangeStart, int assetRangeLength)
        {
            Event = @event;
            AssetRangeStart = assetRangeStart;
            AssetRangeLength = assetRangeLength;
        }

        public TradeOccurred Event { get; }
        public int AssetRangeStart { get; }
        public int AssetRangeLength { get; }
    }

    private readonly struct BookDispatch
    {
        public BookDispatch(BookUpdated @event, int assetRangeStart, int assetRangeLength)
        {
            Event = @event;
            AssetRangeStart = assetRangeStart;
            AssetRangeLength = assetRangeLength;
        }

        public BookUpdated Event { get; }
        public int AssetRangeStart { get; }
        public int AssetRangeLength { get; }
    }

    private readonly struct BookDeltaDispatch
    {
        public BookDeltaDispatch(BookDeltaReceived @event, int assetRangeStart, int assetRangeLength)
        {
            Event = @event;
            AssetRangeStart = assetRangeStart;
            AssetRangeLength = assetRangeLength;
        }

        public BookDeltaReceived Event { get; }
        public int AssetRangeStart { get; }
        public int AssetRangeLength { get; }
    }

    private readonly struct BookDeltasDispatch
    {
        public BookDeltasDispatch(BookDeltasReceived @event, int assetRangeStart, int assetRangeLength)
        {
            Event = @event;
            AssetRangeStart = assetRangeStart;
            AssetRangeLength = assetRangeLength;
        }

        public BookDeltasReceived Event { get; }
        public int AssetRangeStart { get; }
        public int AssetRangeLength { get; }
    }

    internal void RunExecutionGuarded(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        ExecutionEvent evt,
        in StateTransitionResult transition)
    {
#if DEBUG
        var guardWarmed = IsExecutionGuardWarmed(GetType());
        long start = guardWarmed ? GC.GetAllocatedBytesForCurrentThread() : 0;
#endif

        switch (evt)
        {
            case OrderAccepted accepted:
            {
                var order = new OrderContext(accepted.StrategyId, accepted.OrderId, OrderStatus.Open, accepted.VariantId);
                OnOrderAccepted(ref order);
                break;
            }

            case OrderModified modified:
            {
                var order = new OrderContext(modified.StrategyId, modified.OrderId, OrderStatus.Open, modified.VariantId);
                OnOrderModified(ref order);
                break;
            }

            case OrderRejected rejected:
            {
                var order = new OrderContext(rejected.StrategyId, rejected.OrderId, OrderStatus.Rejected, rejected.VariantId, rejected.Reason);
                OnOrderRejected(ref order);
                break;
            }

            case OrderCancelled cancelled:
            {
                var order = new OrderContext(cancelled.StrategyId, cancelled.OrderId, OrderStatus.Cancelled, cancelled.VariantId, cancelled.Reason);
                OnOrderCancelled(ref order);
                break;
            }

            case OrderExpired expired:
            {
                var order = new OrderContext(expired.StrategyId, expired.OrderId, OrderStatus.Expired, expired.VariantId);
                OnOrderExpired(ref order);
                break;
            }

            case OrderFilled filled:
            {
                var fill = new FillContext(
                    filled.StrategyId,
                    filled.OrderId,
                    transition.PositionTransition.AssetId,
                    filled.Side,
                    filled.FilledQty,
                    filled.FillPrice,
                    filled.Commission,
                    transition.PositionTransition.Current);
                OnOrderFilled(ref fill);
                break;
            }
        }

        if (transition.PositionTransition.Kind != PositionTransitionKind.None)
        {
            var position = new PositionContext(transition.PositionTransition);
            switch (transition.PositionTransition.Kind)
            {
                case PositionTransitionKind.Opened:
                    OnPositionOpened(ref position);
                    break;
                case PositionTransitionKind.Changed:
                    OnPositionChanged(ref position);
                    break;
                case PositionTransitionKind.Closed:
                    OnPositionClosed(ref position);
                    break;
            }
        }

#if DEBUG
        if (guardWarmed)
        {
            long diff = GC.GetAllocatedBytesForCurrentThread() - start;
            if (diff > 0)
                throw new HotPathAllocationException(diff);
        }
        else
        {
            MarkExecutionGuardWarmed(GetType());
        }
#endif
    }

    internal void RunLifecycleGuarded(in MarketKernel market, ref PortfolioContext portfolio, LifecycleEvent evt)
    {
#if DEBUG
        var guardWarmed = IsLifecycleGuardWarmed(GetType());
        long start = guardWarmed ? GC.GetAllocatedBytesForCurrentThread() : 0;
#endif

        if (evt is Scheduled scheduled)
        {
            var timer = new TimerContext(in market, ref portfolio, scheduled);
            OnScheduled(ref timer);
#if DEBUG
            if (guardWarmed)
            {
                long scheduledDiff = GC.GetAllocatedBytesForCurrentThread() - start;
                if (scheduledDiff > 0)
                    throw new HotPathAllocationException(scheduledDiff);
            }
            else
            {
                MarkLifecycleGuardWarmed(GetType());
            }
#endif
            return;
        }

        var lifecycle = new LifecycleContext(evt);
        switch (evt)
        {
            case SessionStarted:
                OnStart(ref lifecycle);
                break;
            case SessionEnded:
                OnStop(ref lifecycle);
                break;
        }

#if DEBUG
        if (guardWarmed)
        {
            long diff = GC.GetAllocatedBytesForCurrentThread() - start;
            if (diff > 0)
                throw new HotPathAllocationException(diff);
        }
        else
        {
            MarkLifecycleGuardWarmed(GetType());
        }
#endif
    }

#if DEBUG
    private static bool IsMarketGuardWarmed(Type strategyType)
    {
        lock (MarketGuardWarmupLock)
            return MarketGuardWarmedTypes.Contains(strategyType);
    }

    private static void MarkMarketGuardWarmed(Type strategyType)
    {
        lock (MarketGuardWarmupLock)
            MarketGuardWarmedTypes.Add(strategyType);
    }

    private static bool IsExecutionGuardWarmed(Type strategyType)
    {
        lock (ExecutionGuardWarmupLock)
            return ExecutionGuardWarmedTypes.Contains(strategyType);
    }

    private static void MarkExecutionGuardWarmed(Type strategyType)
    {
        lock (ExecutionGuardWarmupLock)
            ExecutionGuardWarmedTypes.Add(strategyType);
    }

    private static bool IsLifecycleGuardWarmed(Type strategyType)
    {
        lock (LifecycleGuardWarmupLock)
            return LifecycleGuardWarmedTypes.Contains(strategyType);
    }

    private static void MarkLifecycleGuardWarmed(Type strategyType)
    {
        lock (LifecycleGuardWarmupLock)
            LifecycleGuardWarmedTypes.Add(strategyType);
    }
#endif

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void __GeneratedInitialize(in MarketKernel market) { }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void __GeneratedRunTick(in MarketKernel market, ref PortfolioContext portfolio) { }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void __GeneratedRunQuote(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        QuoteReceived evt,
        int assetRangeStart,
        int assetRangeLength) { }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void __GeneratedRunTrade(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        TradeOccurred evt,
        int assetRangeStart,
        int assetRangeLength) { }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void __GeneratedRunBook(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        BookUpdated evt,
        int assetRangeStart,
        int assetRangeLength) { }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void __GeneratedRunBookDelta(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        BookDeltaReceived evt,
        int assetRangeStart,
        int assetRangeLength) { }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void __GeneratedRunBookDeltas(
        in MarketKernel market,
        ref PortfolioContext portfolio,
        BookDeltasReceived evt,
        int assetRangeStart,
        int assetRangeLength) { }

    [CompilerGenerated]
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected virtual void __GeneratedRunBars(in MarketKernel market, ref PortfolioContext portfolio) { }

    protected void RunGroupHook(in MarketKernel market, ref PortfolioContext portfolio)
    {
        if (portfolio.ChildIds.IsEmpty)
            return;

        var group = new GroupContext(ref portfolio);
        OnGroup(ref group);
    }

    protected virtual void OnStart(ref LifecycleContext lifecycle) { }
    protected virtual void OnStop(ref LifecycleContext lifecycle) { }
    protected virtual void OnScheduled(ref TimerContext timer) { }

    protected virtual void OnOrderAccepted(ref OrderContext order) { }
    protected virtual void OnOrderModified(ref OrderContext order) { }
    protected virtual void OnOrderRejected(ref OrderContext order) { }
    protected virtual void OnOrderCancelled(ref OrderContext order) { }
    protected virtual void OnOrderExpired(ref OrderContext order) { }
    protected virtual void OnOrderFilled(ref FillContext fill) { }

    protected virtual void OnPositionOpened(ref PositionContext position) { }
    protected virtual void OnPositionChanged(ref PositionContext position) { }
    protected virtual void OnPositionClosed(ref PositionContext position) { }
    protected virtual void OnGroup(ref GroupContext group) { }
}
