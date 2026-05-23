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

    private readonly Dictionary<StrategyId, UnmanagedPagedStore<PositionState>> _strategyPositionPages = new();
    private readonly Dictionary<StrategyId, UnmanagedPagedStore<OrderState>> _strategyOrderPages = new();
    private readonly Dictionary<StrategyId, Money> _strategyCash = new();
    private readonly Dictionary<StrategyId, PagedTensorStore> _strategyTensors = new();
    private readonly Dictionary<StrategyId, int> _strategyTensorSizes = new();
    private readonly Dictionary<StrategyId, Position[]> _snapshotPositionBuffers = new();
    private readonly Dictionary<StrategyId, Instrument[]> _snapshotInstrumentBuffers = new();
    private readonly Dictionary<StrategyId, decimal> _allocationWeights = new();
    private readonly Dictionary<StrategyId, Money?> _maxCapital = new();
    private readonly HashSet<StrategyId> _pausedStrategies = new();
    private readonly Dictionary<StrategyId, OrderIntent[]> _pendingOrderIntents = new();
    private readonly Dictionary<StrategyId, int> _pendingOrderIntentCounts = new();
    private readonly GlobalMemoryTracker? _tracker;
    private readonly object _gate = new();

    public WorldState()
    {
    }

    internal WorldState(GlobalMemoryTracker? tracker)
    {
        _tracker = tracker;
    }

    public void AllocatePage(StrategyId strategyId, int pageIndex)
    {
        lock (_gate)
        {
            EnsureStrategySlice(strategyId);
            var posPages = _strategyPositionPages[strategyId];
            posPages.EnsurePage(pageIndex);
            _strategyOrderPages[strategyId].EnsurePage(pageIndex);
        }
    }

    public ref PositionState PositionAt(StrategyId strategyId, int virtualIndex)
    {
        AllocatePage(strategyId, virtualIndex / PageSize);
        return ref _strategyPositionPages[strategyId].ValueAt(virtualIndex);
    }

    public ref OrderState OrderAt(StrategyId strategyId, int virtualIndex)
    {
        AllocatePage(strategyId, virtualIndex / PageSize);
        return ref _strategyOrderPages[strategyId].ValueAt(virtualIndex);
    }

    public void RegisterStrategyField<T>(StrategyId strategyId, VectorField<T> field, int universeSize)
        where T : unmanaged
    {
        lock (_gate)
        {
            var tensors = EnsureStrategyTensorStore(strategyId);
            EnsureStrategyTensorCapacity(strategyId, universeSize);
            if (universeSize > 0)
                _ = tensors.GetScalar(field, 0);
        }
    }

    public void EnsureSnapshotCapacity(StrategyId strategyId, int universeSize)
    {
        lock (_gate)
        {
            EnsureStrategySlice(strategyId);
            if (_snapshotPositionBuffers.TryGetValue(strategyId, out var existingPositions) &&
                existingPositions.Length >= universeSize)
            {
                return;
            }

            _snapshotInstrumentBuffers.TryGetValue(strategyId, out var existingInstruments);

            var nextPositions = new Position[universeSize];
            var nextInstruments = new Instrument[universeSize];
            if (existingPositions is not null)
                Array.Copy(existingPositions, nextPositions, existingPositions.Length);
            if (existingInstruments is not null)
                Array.Copy(existingInstruments, nextInstruments, existingInstruments.Length);

            for (var i = existingPositions?.Length ?? 0; i < nextPositions.Length; i++)
                nextPositions[i] = Position.Empty(Instrument.Unknown);
            for (var i = existingInstruments?.Length ?? 0; i < nextInstruments.Length; i++)
                nextInstruments[i] = new Instrument(new Asset($"Virtual-{i}", AssetClass.Equity), Venue.Unknown);

            _snapshotPositionBuffers[strategyId] = nextPositions;
            _snapshotInstrumentBuffers[strategyId] = nextInstruments;
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
        AllocatePage(strategyId, 0);
        Span<PositionState> positions;
        Span<OrderState> orders;
        PagedTensorStore strategyTensors;
        Money cash;
        decimal allocationWeight;
        Money? maxCapital;
        bool isPaused;

        lock (_gate)
        {
            positions = _strategyPositionPages[strategyId].PageSpan(0);
            orders = _strategyOrderPages[strategyId].PageSpan(0);
            strategyTensors = EnsureStrategyTensorStore(strategyId);
            cash = _strategyCash.TryGetValue(strategyId, out var value)
                ? value
                : Money.Zero(Currency.USD);
            allocationWeight = _allocationWeights.TryGetValue(strategyId, out var weight)
                ? weight
                : 1m;
            _maxCapital.TryGetValue(strategyId, out maxCapital);
            isPaused = _pausedStrategies.Contains(strategyId);
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
        lock (_gate)
        {
            _strategyCash[strategyId] = portfolio.Cash;
            _allocationWeights[strategyId] = portfolio.AllocationWeight;
            _maxCapital[strategyId] = portfolio.MaxCapital;

            if (portfolio.IsPaused)
                _pausedStrategies.Add(strategyId);
            else
                _pausedStrategies.Remove(strategyId);
        }

        foreach (var intent in portfolio.DrainOrderIntents())
            AddOrderIntent(intent);
    }

    public int DrainOrderIntents(StrategyId strategyId, Span<OrderIntent> destination)
    {
        lock (_gate)
        {
            if (!_pendingOrderIntents.TryGetValue(strategyId, out var source) ||
                !_pendingOrderIntentCounts.TryGetValue(strategyId, out var count) ||
                count == 0)
            {
                return 0;
            }

            if (destination.Length < count)
                throw new InvalidOperationException("Order intent destination buffer is too small.");

            source.AsSpan(0, count).CopyTo(destination);
            _pendingOrderIntentCounts[strategyId] = 0;
            return count;
        }
    }

    private void AddOrderIntent(OrderIntent intent)
    {
        lock (_gate)
        {
            if (!_pendingOrderIntents.TryGetValue(intent.StrategyId, out var buffer))
            {
                buffer = new OrderIntent[32];
                _pendingOrderIntents[intent.StrategyId] = buffer;
                _pendingOrderIntentCounts[intent.StrategyId] = 0;
            }

            var count = _pendingOrderIntentCounts[intent.StrategyId];
            if (count >= buffer.Length)
                throw new InvalidOperationException("WorldState order intent buffer is full.");

            buffer[count] = intent;
            _pendingOrderIntentCounts[intent.StrategyId] = count + 1;
        }
    }

    public void ApplyAllocationCommand(in AllocationCommand command)
    {
        lock (_gate)
        {
            EnsureStrategySlice(command.TargetStrategy);

            if (command.HasAllocationWeight)
                _allocationWeights[command.TargetStrategy] = command.AllocationWeight;

            if (command.HasMaxCapital)
                _maxCapital[command.TargetStrategy] = Money.USD(command.MaxCapitalAmount);

            if (command.HasPause)
            {
                if (command.Pause)
                    _pausedStrategies.Add(command.TargetStrategy);
                else
                    _pausedStrategies.Remove(command.TargetStrategy);
            }
        }
    }

    public PortfolioSnapshot BuildSnapshot(StrategyId strategyId, int universeSize)
    {
        if (universeSize > 0)
            AllocatePage(strategyId, (universeSize - 1) / PageSize);
        else
            EnsureStrategySlice(strategyId);

        EnsureSnapshotCapacity(strategyId, universeSize);

        var positionCount = 0;
        var grossExposure = 0m;
        var netExposure = 0m;
        var realizedPnL = 0m;

        for (var i = 0; i < universeSize; i++)
        {
            ref var state = ref PositionAt(strategyId, i);
            if (state.IsFlat) continue;

            var notional = state.Quantity * state.AvgEntryPrice;
            grossExposure += Math.Abs(notional);
            netExposure += notional;
            realizedPnL += state.RealizedPnL;
            positionCount++;
        }

        var positions = _snapshotPositionBuffers[strategyId];
        var instruments = _snapshotInstrumentBuffers[strategyId];
        if (positionCount > 0)
        {
            var positionIndex = 0;
            for (var i = 0; i < universeSize; i++)
            {
                ref var state = ref PositionAt(strategyId, i);
                if (state.IsFlat) continue;

                positions[positionIndex++].ResetSnapshot(
                    instruments[i],
                    new Qty(state.Quantity),
                    new Price(state.AvgEntryPrice, Currency.USD),
                    Money.USD(state.RealizedPnL),
                    Instant.Now);
            }
        }

        var cash = _strategyCash.TryGetValue(strategyId, out var cashValue)
            ? cashValue
            : Money.Zero(Currency.USD);

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

    public void Pin() { }
    public void Unpin() { }

    public void Dispose()
    {
        foreach (var pages in _strategyPositionPages.Values)
            pages.Dispose();

        foreach (var pages in _strategyOrderPages.Values)
            pages.Dispose();

        _strategyPositionPages.Clear();
        _strategyOrderPages.Clear();
        _strategyCash.Clear();
        foreach (var tensors in _strategyTensors.Values)
            tensors.Dispose();
        _strategyTensors.Clear();
        _strategyTensorSizes.Clear();
        _snapshotPositionBuffers.Clear();
        _snapshotInstrumentBuffers.Clear();
        _allocationWeights.Clear();
        _maxCapital.Clear();
        _pausedStrategies.Clear();
    }

    private void EnsureStrategySlice(StrategyId id)
    {
        if (_strategyPositionPages.ContainsKey(id)) return;
        _strategyPositionPages[id] = new UnmanagedPagedStore<PositionState>(PageSize, _tracker);
        _strategyOrderPages[id] = new UnmanagedPagedStore<OrderState>(PageSize, _tracker);
        _strategyCash[id] = Money.Zero(Currency.USD);
        _allocationWeights[id] = 1m;
        _maxCapital[id] = null;
    }

    private PagedTensorStore EnsureStrategyTensorStore(StrategyId id)
    {
        EnsureStrategySlice(id);
        if (_strategyTensors.TryGetValue(id, out var tensors))
            return tensors;

        tensors = _tracker is null ? new PagedTensorStore() : new PagedTensorStore(_tracker);
        _strategyTensors[id] = tensors;
        _strategyTensorSizes[id] = 0;
        return tensors;
    }

    private void EnsureStrategyTensorCapacity(StrategyId id, int universeSize)
    {
        var tensors = EnsureStrategyTensorStore(id);
        var current = _strategyTensorSizes[id];
        while (current < universeSize)
        {
            tensors.Grow();
            current++;
        }

        _strategyTensorSizes[id] = current;
    }
}
