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
    private readonly Dictionary<int, SecurityMetadata> _metadata = new();
    private readonly Dictionary<int, IHftDepth> _depths = new();

    public PagedTensorStore Tensors { get; }
    public PagedTensorStore MarketState { get; }
    public BatchMap BatchMap { get; } = new();
    public MarketBatchMap MarketBatchMap { get; } = new();
    public WorldState WorldState { get; }

    public RhodiumRuntime()
        : this(null)
    {
    }

    internal RhodiumRuntime(GlobalMemoryTracker? memoryTracker)
    {
        Tensors = new PagedTensorStore(memoryTracker);
        MarketState = new PagedTensorStore(memoryTracker);
        WorldState = new WorldState(memoryTracker);
    }

    public MarketKernel CreateMarketKernel()
        => new(Tensors, MarketState, BatchMap, MarketBatchMap, _metadata, _depths);

    public void SetMetadata(int virtualIndex, SecurityMetadata metadata) => _metadata[virtualIndex] = metadata;
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

        var metadata = _metadata.TryGetValue(virtualIndex, out var existing)
            ? existing
            : SecurityMetadata.Default(instrument);

        depth = new HashMapDepth(metadata.TickSize, metadata.LotSize);
        _depths[virtualIndex] = depth;
        return depth;
    }

    public void Dispose()
    {
        WorldState.Dispose();
        MarketState.Dispose();
        Tensors.Dispose();
    }
}
