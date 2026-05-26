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
    private readonly IReadOnlyDictionary<int, InstrumentContract> _contracts;
    private readonly IReadOnlyDictionary<int, InstrumentContractProjection> _contractProjections;
    private readonly IReadOnlyDictionary<int, IHftDepth> _depths;
    private readonly Instant _time;

    internal MarketKernel(
        PagedTensorStore tensors,
        PagedTensorStore marketState,
        BatchMap batchMap,
        MarketBatchMap marketBatchMap,
        IReadOnlyDictionary<int, InstrumentContract> contracts,
        IReadOnlyDictionary<int, InstrumentContractProjection> contractProjections,
        IReadOnlyDictionary<int, IHftDepth> depths,
        Instant time)
    {
        _tensors = tensors;
        _marketState = marketState;
        _batchMap = batchMap;
        _marketBatchMap = marketBatchMap;
        _contracts = contracts;
        _contractProjections = contractProjections;
        _depths = depths;
        _time = time;
    }

    public Instant Time => _time;
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

    public InstrumentContract GetContract(AssetId id)
        => _contracts.TryGetValue(id.VirtualIndex, out var contract)
            ? contract
            : throw new InvalidOperationException($"AssetId {id.VirtualIndex} has no registered InstrumentContract.");

    public InstrumentContractProjection GetProjection(AssetId id)
        => _contractProjections.TryGetValue(id.VirtualIndex, out var projection)
            ? projection
            : throw new InvalidOperationException($"AssetId {id.VirtualIndex} has no registered InstrumentContract projection.");

    public TradingGrid GetGrid(AssetId id)
    {
        var projection = GetProjection(id);
        return new TradingGrid(
            projection.PriceIncrement,
            projection.SizeIncrement,
            projection.PricePrecision,
            projection.SizePrecision,
            projection.LotSize);
    }

    public decimal GetPriceIncrement(AssetId id) => GetProjection(id).PriceIncrement;
    public decimal GetSizeIncrement(AssetId id) => GetProjection(id).SizeIncrement;
    public decimal GetLotSize(AssetId id) => GetProjection(id).LotSize;
    public decimal GetMultiplier(AssetId id) => GetProjection(id).Multiplier;
    public decimal GetContractUnitOfTrade(AssetId id) => GetProjection(id).ContractUnitOfTrade;
    public EconomicExposureKind GetExposureKind(AssetId id) => GetProjection(id).ExposureKind;
    public Currency GetQuoteCurrency(AssetId id) => GetProjection(id).QuoteCurrency;
    public Currency GetSettlementCurrency(AssetId id) => GetProjection(id).SettlementCurrency;
    public bool IsTradable(AssetId id) => GetProjection(id).IsTradable;
    public bool SupportsExecution(AssetId id) => GetProjection(id).SupportsExecution;
    public bool IsOption(AssetId id) => GetProjection(id).IsOption;
    public bool IsPackage(AssetId id) => GetProjection(id).IsPackage;
    public OrderTypeMask GetAllowedOrderTypes(AssetId id) => GetProjection(id).AllowedOrderTypes;
    public TimeInForceMask GetAllowedTimeInForce(AssetId id) => GetProjection(id).AllowedTimeInForce;

    internal void RunAdjustmentKernel()
        => _tensors.ForEachPage(new AdjustmentKernel());
}
