using Rhodium.HFT;
using Rhodium.Primitives;
using Rhodium.Tensor;
using Rhodium.Unsafe;

namespace Rhodium.Kernel;

/// <summary>
/// Top-level disposable owner for the Unified Kernel runtime.
/// Strategies never receive this object directly.
/// </summary>
public sealed class RhodiumRuntime : IDisposable
{
    private readonly Dictionary<int, InstrumentContract> _contracts = new();
    private readonly Dictionary<int, InstrumentContractProjection> _contractProjections = new();
    private readonly Dictionary<int, IHftDepth> _depths = new();
    private readonly Rhodium.HFT.MarketTensorBasis _l3Basis;
    private readonly L3EventHandler _l3Events;
    private Instant _currentTime;

    public PagedTensorStore Tensors { get; }
    public PagedTensorStore MarketState { get; }
    public BatchMap BatchMap { get; } = new();
    public MarketBatchMap MarketBatchMap { get; } = new();
    public WorldState WorldState { get; }
    public Instant CurrentTime => _currentTime;
    public Rhodium.HFT.MarketTensorBasis L3Basis => _l3Basis;
    public L3EventHandler L3Events => _l3Events;

    public RhodiumRuntime()
        : this(null, null)
    {
    }

    public RhodiumRuntime(MarketTensorSpaceConfig? marketTensorSpace)
        : this(null, marketTensorSpace)
    {
    }

    internal RhodiumRuntime(GlobalMemoryTracker? memoryTracker)
        : this(memoryTracker, null)
    {
    }

    private RhodiumRuntime(GlobalMemoryTracker? memoryTracker, MarketTensorSpaceConfig? marketTensorSpace)
    {
        Tensors = new PagedTensorStore(memoryTracker);
        MarketState = new PagedTensorStore(memoryTracker);
        WorldState = new WorldState(memoryTracker);
        _l3Basis = new Rhodium.HFT.MarketTensorBasis(marketTensorSpace ?? new MarketTensorSpaceConfig());
        _l3Events = new L3EventHandler(MarketState, _l3Basis);
    }

    public MarketKernel CreateMarketKernel()
        => new(Tensors, MarketState, BatchMap, MarketBatchMap, _contracts, _contractProjections, _depths, _currentTime);

    public PortfolioSnapshot BuildSnapshot(
        StrategyId strategyId,
        int universeSize,
        IReadOnlyDictionary<int, Price>? marks = null) =>
        WorldState.BuildSnapshot(strategyId, universeSize, _currentTime, _contracts, marks);

    public void SetTime(Instant time)
        => _currentTime = time;

    public void SetContract(int virtualIndex, InstrumentContract contract)
    {
        _contracts[virtualIndex] = contract;
        _contractProjections[virtualIndex] = InstrumentContractProjection.From(contract);
    }

    public bool TryGetContract(Instrument instrument, out InstrumentContract contract, int variantId = 0)
    {
        try
        {
            var range = BatchMap.GetInstrumentRange(instrument);
            var virtualIndex = range.Start + variantId;
            return _contracts.TryGetValue(virtualIndex, out contract!);
        }
        catch (KeyNotFoundException)
        {
            contract = null!;
            return false;
        }
    }

    public void SetDepth(int virtualIndex, IHftDepth depth) => _depths[virtualIndex] = depth;

    internal void ClearDepth(int virtualIndex, Instrument instrument)
    {
        var depth = GetOrCreateDepth(virtualIndex, instrument);
        depth.Clear();
    }

    internal void UpdateDepthLevel(int virtualIndex, Instrument instrument, Side side, Price price, Qty size, Instant timestamp)
    {
        var depth = GetOrCreateDepth(virtualIndex, instrument);
        depth.Update(side, TickPrice.FromPrice(price, depth.TickSize).Ticks, size.Value, timestamp);
    }

    internal IHftDepth GetOrCreateDepth(int virtualIndex, Instrument instrument)
    {
        if (_depths.TryGetValue(virtualIndex, out var depth))
            return depth;

        var projection = _contractProjections.TryGetValue(virtualIndex, out var existing)
            ? existing
            : throw new InvalidOperationException(
                $"Virtual index {virtualIndex} for instrument {instrument} has no registered InstrumentContract projection.");

        depth = new HashMapDepth(projection.PriceIncrement, projection.SizeIncrement);
        _depths[virtualIndex] = depth;
        return depth;
    }

    internal void AddBookOrder(Instrument instrument, BookOrder order, Instant timestamp)
        => _l3Events.OnOrderAdd(
            instrument.ToString(),
            order.OrderId.Value,
            (double)order.Price.Value,
            (double)order.Size.Value,
            order.Side == Side.Buy ? OrderSide.Buy : OrderSide.Sell,
            timestamp.Nanos);

    internal void ModifyBookOrder(Instrument instrument, BookOrder order, Instant timestamp)
    {
        DeleteBookOrder(instrument, order.OrderId);
        if (order.Size.Value > 0m)
            AddBookOrder(instrument, order, timestamp);
    }

    internal void DeleteBookOrder(Instrument instrument, BookOrderId orderId)
        => _l3Events.OnOrderDelete(instrument.ToString(), orderId.Value);

    internal void ExecuteBookOrder(Instrument instrument, BookOrderId orderId, Qty executedSize)
        => _l3Events.OnOrderExecute(instrument.ToString(), orderId.Value, (double)executedSize.Value);

    public void Dispose()
    {
        WorldState.Dispose();
        MarketState.Dispose();
        Tensors.Dispose();
    }
}
