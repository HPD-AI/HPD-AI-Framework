using Rhodium.Tensor;

namespace Rhodium.HFT;

/// <summary>
/// Handles L3 market data events and updates market tensor space.
/// </summary>
public sealed class L3EventHandler
{
    private readonly record struct OrderLocation(
        string InstrumentId,
        int PriceLevel,
        int Slot,
        double Price,
        OrderSide Side);

    private readonly ITensorStore _marketTensorStore;
    private readonly MarketTensorBasis _basis;
    private readonly Dictionary<long, OrderLocation> _orders = new();
    private readonly Dictionary<string, Dictionary<double, int>> _priceLevels = new();
    private int _allocatedVirtualCount;

    public L3EventHandler(ITensorStore marketTensorStore, MarketTensorBasis basis)
    {
        _marketTensorStore = marketTensorStore;
        _basis = basis;
    }

    /// <summary>
    /// Process L3 order add event.
    /// </summary>
    public void OnOrderAdd(string instrumentId, long orderId, double price, double size,
                           OrderSide side, long timestamp)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Order size must be positive.");
        if (_orders.ContainsKey(orderId))
            throw new InvalidOperationException($"Order already exists: {orderId}");

        _basis.RegisterInstrument(instrumentId);
        var priceLevel = GetOrAddPriceLevel(instrumentId, price);
        var slot = FindEmptySlot(instrumentId, priceLevel);
        var vi = _basis.GetVI(instrumentId, priceLevel, slot);
        EnsureAllocated(vi);

        _marketTensorStore.GetScalar(MarketField.OrderId, vi) = new FactorF64(orderId);
        _marketTensorStore.GetScalar(MarketField.OrderQty, vi) = new SizeF64(size);
        _marketTensorStore.GetScalar(MarketField.OrderTimestamp, vi) = new FactorF64(timestamp);

        _orders[orderId] = new OrderLocation(instrumentId, priceLevel, slot, price, side);
        UpdateLevelAggregates(instrumentId, priceLevel);
    }

    /// <summary>
    /// Process L3 order modify event.
    /// </summary>
    public void OnOrderModify(string instrumentId, long orderId, double newSize)
    {
        if (newSize < 0)
            throw new ArgumentOutOfRangeException(nameof(newSize), "Order size cannot be negative.");
        if (!_orders.TryGetValue(orderId, out var location) || location.InstrumentId != instrumentId)
            return;

        if (newSize == 0)
        {
            OnOrderDelete(instrumentId, orderId);
            return;
        }

        var vi = _basis.GetVI(location.InstrumentId, location.PriceLevel, location.Slot);
        _marketTensorStore.GetScalar(MarketField.OrderQty, vi) = new SizeF64(newSize);
        UpdateLevelAggregates(location.InstrumentId, location.PriceLevel);
    }

    /// <summary>
    /// Process L3 order delete event.
    /// </summary>
    public void OnOrderDelete(string instrumentId, long orderId)
    {
        if (!_orders.Remove(orderId, out var location) || location.InstrumentId != instrumentId)
            return;

        ClearSlot(location);
        UpdateLevelAggregates(location.InstrumentId, location.PriceLevel);
    }

    /// <summary>
    /// Process L3 order execute event.
    /// </summary>
    public void OnOrderExecute(string instrumentId, long orderId, double executedSize)
    {
        if (executedSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(executedSize), "Executed size must be positive.");
        if (!_orders.TryGetValue(orderId, out var location) || location.InstrumentId != instrumentId)
            return;

        var vi = _basis.GetVI(location.InstrumentId, location.PriceLevel, location.Slot);
        var remaining = _marketTensorStore.GetScalar(MarketField.OrderQty, vi).Value - executedSize;
        if (remaining <= 0)
        {
            _orders.Remove(orderId);
            ClearSlot(location);
        }
        else
        {
            _marketTensorStore.GetScalar(MarketField.OrderQty, vi) = new SizeF64(remaining);
        }

        UpdateLevelAggregates(location.InstrumentId, location.PriceLevel);
    }

    private int GetOrAddPriceLevel(string instrumentId, double price)
    {
        if (!_priceLevels.TryGetValue(instrumentId, out var levels))
        {
            levels = new Dictionary<double, int>();
            _priceLevels[instrumentId] = levels;
        }

        if (levels.TryGetValue(price, out var existing))
            return existing;

        var next = levels.Count;
        if (next >= _basis.PriceLevelsPerInstrument)
            throw new InvalidOperationException(
                $"Cannot track more than {_basis.PriceLevelsPerInstrument} price levels for {instrumentId}.");

        levels[price] = next;
        return next;
    }

    private int FindEmptySlot(string instrumentId, int priceLevel)
    {
        for (var slot = 0; slot < _basis.OrderSlotsPerLevel; slot++)
        {
            var vi = _basis.GetVI(instrumentId, priceLevel, slot);
            EnsureAllocated(vi);
            if (_marketTensorStore.GetScalar(MarketField.OrderId, vi).Value == 0.0)
                return slot;
        }

        throw new InvalidOperationException(
            $"No free L3 order slots for {instrumentId} price level {priceLevel}.");
    }

    private void ClearSlot(OrderLocation location)
    {
        var vi = _basis.GetVI(location.InstrumentId, location.PriceLevel, location.Slot);
        _marketTensorStore.GetScalar(MarketField.OrderId, vi) = new FactorF64(0.0);
        _marketTensorStore.GetScalar(MarketField.OrderQty, vi) = new SizeF64(0.0);
        _marketTensorStore.GetScalar(MarketField.OrderTimestamp, vi) = new FactorF64(0.0);
    }

    private void UpdateLevelAggregates(string instrumentId, int priceLevel)
    {
        var total = 0.0;
        var count = 0;

        for (var slot = 0; slot < _basis.OrderSlotsPerLevel; slot++)
        {
            var vi = _basis.GetVI(instrumentId, priceLevel, slot);
            EnsureAllocated(vi);
            var orderId = _marketTensorStore.GetScalar(MarketField.OrderId, vi).Value;
            if (orderId == 0.0)
                continue;

            total += _marketTensorStore.GetScalar(MarketField.OrderQty, vi).Value;
            count++;
        }

        var aggregateVi = _basis.GetVI(instrumentId, priceLevel, 0);
        _marketTensorStore.GetScalar(MarketField.TotalQtyAtLevel, aggregateVi) = new SizeF64(total);
        _marketTensorStore.GetScalar(MarketField.OrderCount, aggregateVi) = new FactorF64(count);
    }

    private void EnsureAllocated(int virtualIndex)
    {
        while (_allocatedVirtualCount <= virtualIndex)
        {
            _marketTensorStore.Grow();
            _allocatedVirtualCount++;
        }
    }
}
