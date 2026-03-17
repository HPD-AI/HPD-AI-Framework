using System.Runtime.InteropServices;
using Rhodium.HFT;
using Rhodium.Primitives;
using Rhodium.Tensor;

namespace Rhodium.Kernel;

/// <summary>
/// Core kernel trading engine with zero-dispatch hot path.
/// Hard-bound struct for maximum performance.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TradingEngine
{
    // ==================== STORAGE (DUAL TENSOR SPACES) ====================

    /// <summary>Strategy state (orders, positions, indicators).</summary>
    public PagedTensorStore Tensors;

    /// <summary>Market L2/L3 order book state.</summary>
    public PagedTensorStore MarketState;

    /// <summary>Strategy virtual index mapping.</summary>
    public BatchMap BatchMap;

    /// <summary>Market virtual index mapping (L3 order book).</summary>
    public MarketBatchMap MarketBatchMap;

    // ==================== METADATA (FLATTENED SOURCE OF TRUTH) ====================

    private readonly Dictionary<int, SecurityMetadata> _metadata;
    private readonly Dictionary<int, IHftDepth> _depths;
    private readonly Dictionary<int, decimal> _positions;
    private readonly Dictionary<int, HashSet<OrderId>> _activeOrders;

    public TradingEngine()
    {
        Tensors = new PagedTensorStore();
        MarketState = new PagedTensorStore();
        BatchMap = new BatchMap();
        MarketBatchMap = new MarketBatchMap();
        _metadata = new Dictionary<int, SecurityMetadata>();
        _depths = new Dictionary<int, IHftDepth>();
        _positions = new Dictionary<int, decimal>();
        _activeOrders = new Dictionary<int, HashSet<OrderId>>();
    }

    // ==================== METADATA ACCESS ====================

    public decimal GetTickSize(int index)
    {
        if (_metadata.TryGetValue(index, out var meta))
            return meta.TickSize;
        return 0.01m;
    }

    public decimal GetLotSize(int index)
    {
        if (_metadata.TryGetValue(index, out var meta))
            return meta.LotSize;
        return 1m;
    }

    public Currency GetCurrency(int index)
    {
        if (_metadata.TryGetValue(index, out var meta))
            return meta.Currency;
        return Currency.USD;
    }

    public void SetMetadata(int index, SecurityMetadata metadata)
    {
        _metadata[index] = metadata;
    }

    // ==================== FLATTENED L1/L2 ACCESS (DIRECT READS - ZERO DISPATCH) ====================

    public long? GetBestBidTick(int index)
    {
        if (_depths.TryGetValue(index, out var depth))
            return depth.BestBidTick;
        return null;
    }

    public long? GetBestAskTick(int index)
    {
        if (_depths.TryGetValue(index, out var depth))
            return depth.BestAskTick;
        return null;
    }

    public decimal GetQtyAtTick(int index, Side side, long tick)
    {
        if (_depths.TryGetValue(index, out var depth))
            return depth.QtyAtTick(side, tick);
        return 0m;
    }

    // ==================== SLOW PATH (INTERFACE RETURN - NOT ZERO-DISPATCH) ====================

    public IHftDepth? GetDepth(int index)
    {
        _depths.TryGetValue(index, out var depth);
        return depth;
    }

    public void SetDepth(int index, IHftDepth depth)
    {
        _depths[index] = depth;
    }

    // ==================== EXECUTION & POSITION ====================

    public decimal GetPosition(int index)
    {
        if (_positions.TryGetValue(index, out var pos))
            return pos;
        return 0m;
    }

    public void SetPosition(int index, decimal position)
    {
        _positions[index] = position;
    }

    public bool HasOpenOrder(int index, Side side)
    {
        if (!_activeOrders.TryGetValue(index, out var orders))
            return false;

        // This is simplified - in production, would check order side
        return orders.Count > 0;
    }

    public void SubmitLimitOrder(int index, Side side, Qty qty, Price limitPrice)
    {
        // Placeholder - actual implementation would route to execution system
        var orderId = OrderId.New();
        if (!_activeOrders.ContainsKey(index))
            _activeOrders[index] = new HashSet<OrderId>();
        _activeOrders[index].Add(orderId);
    }

    public void SubmitMarketOrder(int index, Side side, Qty qty)
    {
        // Placeholder - actual implementation would route to execution system
        var orderId = OrderId.New();
        if (!_activeOrders.ContainsKey(index))
            _activeOrders[index] = new HashSet<OrderId>();
        _activeOrders[index].Add(orderId);
    }

    public void CancelOrder(int index, OrderId orderId)
    {
        if (_activeOrders.TryGetValue(index, out var orders))
            orders.Remove(orderId);
    }

    public void CancelAllOrders(int index)
    {
        if (_activeOrders.TryGetValue(index, out var orders))
            orders.Clear();
    }

    // ==================== MEMORY MANAGEMENT (AOT ROOTING CONTRACT) ====================

    /// <summary>
    /// Ensures a column exists for the given field.
    /// Allocates column immediately and registers for auto-growth.
    /// Must be called during strategy OnInitialize.
    /// </summary>
    public void EnsureColumn<T>(VectorField<T> field) where T : unmanaged
    {
        // Force column allocation by accessing it
        if (Tensors.GetScalar(field, 0).Equals(default(T)))
        {
            // Column now exists and is rooted for AOT
        }
    }
}

/// <summary>
/// Market batch map for L3 order book virtual indices.
/// Separate from strategy BatchMap.
/// </summary>
public sealed class MarketBatchMap : IBatchMap
{
    private Rhodium.HFT.MarketTensorBasis _currentBasis;
    private readonly Rhodium.HFT.MarketTensorSpaceConfig _config;
    private int _version;

    public MarketBatchMap(Rhodium.HFT.MarketTensorSpaceConfig? config = null)
    {
        _config = config ?? new();
        _currentBasis = new Rhodium.HFT.MarketTensorBasis(_config);
    }

    public int Version => _version;
    public int TotalSize => _config.TotalMarketVIs;
    public TensorBasis CurrentBasis => new(0, 0); // Placeholder

    public (int Start, int Length) GetInstrumentRange(Instrument instrument)
    {
        // Placeholder implementation
        return (0, 0);
    }

    public (Instrument Inst, int VariantId) GetContext(int virtualIndex)
    {
        // Placeholder implementation
        return (Instrument.Unknown, 0);
    }

    public (Instrument Inst, int VariantId) SafeGetContext(int virtualIndex)
    {
        // Placeholder implementation
        return (Instrument.Unknown, 0);
    }
}
