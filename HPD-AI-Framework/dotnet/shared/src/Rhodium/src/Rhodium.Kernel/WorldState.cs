using Rhodium.Primitives;
using Rhodium.Tensor;
using Rhodium.Unsafe;
using Rhodium.Unsafe.Storage;

namespace Rhodium.Kernel;

/// <summary>
/// Per-strategy unmanaged backing store for hot-path portfolio state.
/// </summary>
public sealed class WorldState : IDisposable
{
    private const int PageSize = 1024;
    private static readonly InstrumentContract SnapshotFallbackContract =
        Contracts.Equity("UNKNOWN", Venue.Unknown, Currency.USD);

    private readonly Dictionary<StrategyId, StrategyStateSlice> _strategies = new();
    private readonly GlobalMemoryTracker? _tracker;
    private readonly object _strategiesGate = new();

    public WorldState()
    {
    }

    internal WorldState(GlobalMemoryTracker? tracker)
    {
        _tracker = tracker;
    }

    public void AllocatePage(StrategyId strategyId, int pageIndex)
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            slice.PositionPages.EnsurePage(pageIndex);
            slice.OrderPages.EnsurePage(pageIndex);
        }
    }

    public ref PositionState PositionAt(StrategyId strategyId, int virtualIndex)
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            slice.PositionPages.EnsurePage(virtualIndex / PageSize);
        }

        return ref slice.PositionPages.ValueAt(virtualIndex);
    }

    public ref OrderState OrderAt(StrategyId strategyId, int virtualIndex)
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            slice.OrderPages.EnsurePage(virtualIndex / PageSize);
        }

        return ref slice.OrderPages.ValueAt(virtualIndex);
    }

    public void AdjustCash(StrategyId strategyId, Money delta)
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            var current = slice.Cash;
            if (!current.IsZero && current.Currency != delta.Currency)
                throw new InvalidOperationException($"Cash adjustment currency {delta.Currency} does not match strategy cash currency {current.Currency}.");

            slice.Cash = current + delta;
        }
    }

    public void RegisterStrategyField<T>(StrategyId strategyId, VectorField<T> field, int universeSize)
        where T : unmanaged
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            EnsureStrategyTensorCapacity(slice, universeSize);
            if (universeSize > 0)
                _ = slice.StrategyTensors.GetScalar(field, 0);
        }
    }

    public void EnsureSnapshotCapacity(StrategyId strategyId, int universeSize)
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            if (slice.SnapshotPositions.Length >= universeSize)
            {
                return;
            }

            var nextPositions = new Position[universeSize];
            var nextInstruments = new Instrument[universeSize];
            Array.Copy(slice.SnapshotPositions, nextPositions, slice.SnapshotPositions.Length);
            Array.Copy(slice.SnapshotInstruments, nextInstruments, slice.SnapshotInstruments.Length);

            for (var i = slice.SnapshotPositions.Length; i < nextPositions.Length; i++)
                nextPositions[i] = Position.Empty(Instrument.Unknown);
            for (var i = slice.SnapshotInstruments.Length; i < nextInstruments.Length; i++)
                nextInstruments[i] = new Instrument(new Asset($"Virtual-{i}", AssetClass.Equity), Venue.Unknown);

            slice.SnapshotPositions = nextPositions;
            slice.SnapshotInstruments = nextInstruments;
        }
    }

    public PortfolioContext BuildContext(
        StrategyId strategyId,
        StrategyId? parentId,
        ReadOnlySpan<StrategyId> childIds,
        Span<int> counters,
        Span<AllocationCommand> pendingCommands,
        ReadOnlySpan<PortfolioSnapshot> childSnapshots = default,
        Span<OrderIntent> orderIntents = default)
    {
        var slice = GetOrCreateSlice(strategyId);
        Span<PositionState> positions;
        Span<OrderState> orders;
        PagedTensorStore strategyTensors;
        Money cash;
        decimal allocationWeight;
        Money? maxCapital;
        bool isPaused;

        lock (slice.Gate)
        {
            slice.PositionPages.EnsurePage(0);
            slice.OrderPages.EnsurePage(0);
            positions = slice.PositionPages.PageSpan(0);
            orders = slice.OrderPages.PageSpan(0);
            strategyTensors = slice.StrategyTensors;
            cash = slice.Cash;
            allocationWeight = slice.AllocationWeight;
            maxCapital = slice.MaxCapital;
            isPaused = slice.IsPaused;
        }

        return new PortfolioContext(
            strategyId,
            parentId,
            childIds,
            counters,
            positions,
            orders,
            pendingCommands,
            strategyTensors,
            cash,
            allocationWeight,
            maxCapital,
            isPaused,
            childSnapshots,
            orderIntents);
    }

    public void CommitContext(StrategyId strategyId, ref PortfolioContext portfolio)
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            slice.Cash = portfolio.Cash;
            slice.AllocationWeight = portfolio.AllocationWeight;
            slice.MaxCapital = portfolio.MaxCapital;
            slice.IsPaused = portfolio.IsPaused;
        }

        foreach (var intent in portfolio.DrainOrderIntents())
            AddOrderIntent(intent);
    }

    public int DrainOrderIntents(StrategyId strategyId, Span<OrderIntent> destination)
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            if (slice.PendingOrderIntentCount == 0)
            {
                return 0;
            }

            if (destination.Length < slice.PendingOrderIntentCount)
                throw new InvalidOperationException("Order intent destination buffer is too small.");

            slice.PendingOrderIntents.AsSpan(0, slice.PendingOrderIntentCount).CopyTo(destination);
            var count = slice.PendingOrderIntentCount;
            slice.PendingOrderIntentCount = 0;
            return count;
        }
    }

    private void AddOrderIntent(OrderIntent intent)
    {
        var slice = GetOrCreateSlice(intent.StrategyId);
        lock (slice.Gate)
        {
            if (slice.PendingOrderIntentCount >= slice.PendingOrderIntents.Length)
                throw new InvalidOperationException("WorldState order intent buffer is full.");

            slice.PendingOrderIntents[slice.PendingOrderIntentCount++] = intent;
        }
    }

    public void ApplyAllocationCommand(in AllocationCommand command)
    {
        var slice = GetOrCreateSlice(command.TargetStrategy);
        lock (slice.Gate)
        {
            if (command.HasAllocationWeight)
                slice.AllocationWeight = command.AllocationWeight;

            if (command.HasMaxCapital)
                slice.MaxCapital = Money.USD(command.MaxCapitalAmount);

            if (command.HasPause)
                slice.IsPaused = command.Pause;
        }
    }

    public PortfolioSnapshot BuildSnapshot(StrategyId strategyId, int universeSize, Instant snapshotTime) =>
        BuildSnapshot(strategyId, universeSize, snapshotTime, null, null);

    public PortfolioSnapshot BuildSnapshot(
        StrategyId strategyId,
        int universeSize,
        Instant snapshotTime,
        IReadOnlyDictionary<int, InstrumentContract>? contracts,
        IReadOnlyDictionary<int, Price>? marks = null)
    {
        var slice = GetOrCreateSlice(strategyId);
        lock (slice.Gate)
        {
            if (universeSize > 0)
            {
                slice.PositionPages.EnsurePage((universeSize - 1) / PageSize);
            }

            EnsureSnapshotCapacity(slice, universeSize);

            var positionCount = 0;
            var grossExposure = 0m;
            var netExposure = 0m;
            var realizedPnL = 0m;
            var valuation = DefaultInstrumentValuationModel.Instance;

            for (var i = 0; i < universeSize; i++)
            {
                ref var state = ref slice.PositionPages.ValueAt(i);
                if (state.IsFlat) continue;

                var contract = GetSnapshotContract(i, contracts);
                var mark = GetSnapshotMark(i, state, contract, marks);
                var quantity = new Qty(state.Quantity);
                var notional = valuation.Notional(contract, quantity, mark);
                var marketValue = valuation.MarketValue(contract, quantity, mark);
                grossExposure += Math.Abs(notional.Amount);
                netExposure += marketValue.Amount;
                realizedPnL += state.RealizedPnL;
                positionCount++;
            }

            var positions = slice.SnapshotPositions;
            var instruments = slice.SnapshotInstruments;
            if (positionCount > 0)
            {
                var positionIndex = 0;
                for (var i = 0; i < universeSize; i++)
                {
                    ref var state = ref slice.PositionPages.ValueAt(i);
                    if (state.IsFlat) continue;

                    var contract = GetSnapshotContract(i, contracts);
                    positions[positionIndex++].ResetSnapshot(
                        contract.Instrument == Instrument.Unknown ? instruments[i] : contract.Instrument,
                        new Qty(state.Quantity),
                        new Price(state.AvgEntryPrice, contract.Exposure.QuoteCurrency()),
                        new Money(state.RealizedPnL, contract.Exposure.SettlementCurrency()),
                        snapshotTime);
                }
            }

            var cash = slice.Cash;

            return new PortfolioSnapshot(
                strategyId,
                Money.USD(cash.Amount + netExposure),
                Money.USD(0m),
                Money.USD(realizedPnL),
                grossExposure,
                netExposure,
                default,
                positions,
                positionCount);
        }
    }

    private static InstrumentContract GetSnapshotContract(
        int virtualIndex,
        IReadOnlyDictionary<int, InstrumentContract>? contracts) =>
        contracts is not null && contracts.TryGetValue(virtualIndex, out var contract)
            ? contract
            : SnapshotFallbackContract;

    private static Price GetSnapshotMark(
        int virtualIndex,
        in PositionState state,
        InstrumentContract contract,
        IReadOnlyDictionary<int, Price>? marks)
    {
        if (marks is not null && marks.TryGetValue(virtualIndex, out var mark))
            return mark;

        return new Price(state.AvgEntryPrice, contract.Exposure.QuoteCurrency());
    }

    public void Pin() { }
    public void Unpin() { }

    public void Dispose()
    {
        lock (_strategiesGate)
        {
            foreach (var slice in _strategies.Values)
                slice.Dispose();

            _strategies.Clear();
        }
    }

    private StrategyStateSlice GetOrCreateSlice(StrategyId id)
    {
        lock (_strategiesGate)
        {
            if (_strategies.TryGetValue(id, out var slice))
                return slice;

            slice = new StrategyStateSlice(_tracker);
            _strategies[id] = slice;
            return slice;
        }
    }

    private static void EnsureSnapshotCapacity(StrategyStateSlice slice, int universeSize)
    {
        if (slice.SnapshotPositions.Length >= universeSize)
        {
            return;
        }

        var nextPositions = new Position[universeSize];
        var nextInstruments = new Instrument[universeSize];
        Array.Copy(slice.SnapshotPositions, nextPositions, slice.SnapshotPositions.Length);
        Array.Copy(slice.SnapshotInstruments, nextInstruments, slice.SnapshotInstruments.Length);

        for (var i = slice.SnapshotPositions.Length; i < nextPositions.Length; i++)
            nextPositions[i] = Position.Empty(Instrument.Unknown);
        for (var i = slice.SnapshotInstruments.Length; i < nextInstruments.Length; i++)
            nextInstruments[i] = new Instrument(new Asset($"Virtual-{i}", AssetClass.Equity), Venue.Unknown);

        slice.SnapshotPositions = nextPositions;
        slice.SnapshotInstruments = nextInstruments;
    }

    private static void EnsureStrategyTensorCapacity(StrategyStateSlice slice, int universeSize)
    {
        while (slice.StrategyTensorSize < universeSize)
        {
            slice.StrategyTensors.Grow();
            slice.StrategyTensorSize++;
        }
    }

    private sealed class StrategyStateSlice : IDisposable
    {
        public StrategyStateSlice(GlobalMemoryTracker? tracker)
        {
            PositionPages = new UnmanagedPagedStore<PositionState>(PageSize, tracker);
            OrderPages = new UnmanagedPagedStore<OrderState>(PageSize, tracker);
            StrategyTensors = tracker is null ? new PagedTensorStore() : new PagedTensorStore(tracker);
        }

        public object Gate { get; } = new();
        public UnmanagedPagedStore<PositionState> PositionPages { get; }
        public UnmanagedPagedStore<OrderState> OrderPages { get; }
        public PagedTensorStore StrategyTensors { get; }
        public int StrategyTensorSize { get; set; }
        public Position[] SnapshotPositions { get; set; } = [];
        public Instrument[] SnapshotInstruments { get; set; } = [];
        public Money Cash { get; set; } = Money.Zero(Currency.USD);
        public decimal AllocationWeight { get; set; } = 1m;
        public Money? MaxCapital { get; set; }
        public bool IsPaused { get; set; }
        public OrderIntent[] PendingOrderIntents { get; } = new OrderIntent[32];
        public int PendingOrderIntentCount { get; set; }

        public void Dispose()
        {
            PositionPages.Dispose();
            OrderPages.Dispose();
            StrategyTensors.Dispose();
        }
    }
}
