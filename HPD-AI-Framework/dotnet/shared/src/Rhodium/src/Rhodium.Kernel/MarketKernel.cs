using System.Runtime.InteropServices;
using Rhodium.HFT;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel;

/// <summary>
/// Read-only shared market view passed to strategies.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly ref struct MarketKernel
{
    private readonly PagedTensorStore _tensors;
    private readonly PagedTensorStore _marketState;
    private readonly BatchMap _batchMap;
    private readonly MarketBatchMap _marketBatchMap;
    private readonly IReadOnlyDictionary<int, SecurityMetadata> _metadata;
    private readonly IReadOnlyDictionary<int, IHftDepth> _depths;

    internal MarketKernel(
        PagedTensorStore tensors,
        PagedTensorStore marketState,
        BatchMap batchMap,
        MarketBatchMap marketBatchMap,
        IReadOnlyDictionary<int, SecurityMetadata> metadata,
        IReadOnlyDictionary<int, IHftDepth> depths)
    {
        _tensors = tensors;
        _marketState = marketState;
        _batchMap = batchMap;
        _marketBatchMap = marketBatchMap;
        _metadata = metadata;
        _depths = depths;
    }

    public int UniverseSize => _batchMap.TotalSize;
    public int UniverseVersion => _batchMap.Version;
    public TensorBasis Basis => _batchMap.CurrentBasis;

    public ref readonly T GetScalar<T>(VectorField<T> field, int virtualIndex)
        where T : unmanaged
        => ref _tensors.GetScalar(field, virtualIndex);

    public bool HasField<T>(VectorField<T> field)
        where T : unmanaged
        => _tensors.HasColumn(field);

    public double GetScalar(VectorField<PriceF64> field, AssetId id)
        => _tensors.GetScalar(field, id.VirtualIndex).Value;

    public double GetScalar(VectorField<FactorF64> field, AssetId id)
        => _tensors.GetScalar(field, id.VirtualIndex).Value;

    public double GetScalar(VectorField<SizeF64> field, AssetId id)
        => _tensors.GetScalar(field, id.VirtualIndex).Value;

    public long? GetBestBidTick(AssetId id)
        => _depths.TryGetValue(id.VirtualIndex, out var depth) ? depth.BestBidTick : null;

    public long? GetBestAskTick(AssetId id)
        => _depths.TryGetValue(id.VirtualIndex, out var depth) ? depth.BestAskTick : null;

    public decimal GetQtyAtTick(AssetId id, Side side, long tick)
        => _depths.TryGetValue(id.VirtualIndex, out var depth) ? depth.QtyAtTick(side, tick) : 0m;

    public int CopyDepthLevels(AssetId id, Side side, Span<DepthLevel> destination)
        => _depths.TryGetValue(id.VirtualIndex, out var depth) ? depth.CopyLevels(side, destination) : 0;

    public SecurityMetadata GetMetadata(AssetId id)
        => _metadata.TryGetValue(id.VirtualIndex, out var metadata)
            ? metadata
            : SecurityMetadata.Default(Instrument.Unknown);

    internal void RunAdjustmentKernel()
        => _tensors.ForEachPage(new AdjustmentKernel());
}
