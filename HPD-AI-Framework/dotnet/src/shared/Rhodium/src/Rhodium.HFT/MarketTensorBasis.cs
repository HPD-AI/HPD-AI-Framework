namespace Rhodium.HFT;

/// <summary>
/// Maps (InstrumentId, PriceLevel, OrderSlot) → VI in market tensor space.
/// Provides bidirectional mapping for L3 Market-By-Order data.
/// </summary>
public sealed class MarketTensorBasis
{
    private readonly MarketTensorSpaceConfig _config;
    private readonly Dictionary<string, int> _instrumentIndex = new();

    public MarketTensorBasis(MarketTensorSpaceConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Register instrument and assign index.
    /// Must be called before GetVI for an instrument.
    /// </summary>
    public void RegisterInstrument(string instrumentId)
    {
        if (!_instrumentIndex.ContainsKey(instrumentId))
        {
            if (_instrumentIndex.Count >= _config.InstrumentCount)
                throw new InvalidOperationException(
                    $"Cannot register more than {_config.InstrumentCount} instruments. " +
                    "Increase MarketTensorSpaceConfig.InstrumentCount.");

            _instrumentIndex[instrumentId] = _instrumentIndex.Count;
        }
    }

    /// <summary>
    /// Get VI for (instrument, priceLevel, orderSlot).
    /// VI = instrIdx * (levels * slots) + levelIdx * slots + slotIdx
    /// </summary>
    public int GetVI(string instrumentId, int priceLevel, int orderSlot)
    {
        if (!_instrumentIndex.TryGetValue(instrumentId, out var instrIdx))
            throw new ArgumentException($"Instrument not registered: {instrumentId}");

        if (priceLevel < 0 || priceLevel >= _config.PriceLevelsPerInstrument)
            throw new ArgumentOutOfRangeException(nameof(priceLevel),
                $"Price level {priceLevel} out of range [0, {_config.PriceLevelsPerInstrument})");

        if (orderSlot < 0 || orderSlot >= _config.OrderSlotsPerLevel)
            throw new ArgumentOutOfRangeException(nameof(orderSlot),
                $"Order slot {orderSlot} out of range [0, {_config.OrderSlotsPerLevel})");

        return instrIdx * (_config.PriceLevelsPerInstrument * _config.OrderSlotsPerLevel)
             + priceLevel * _config.OrderSlotsPerLevel
             + orderSlot;
    }

    /// <summary>
    /// Reverse mapping: VI → (instrument, priceLevel, orderSlot).
    /// </summary>
    public (string InstrumentId, int PriceLevel, int OrderSlot) FromVI(int vi)
    {
        var levelsSlots = _config.PriceLevelsPerInstrument * _config.OrderSlotsPerLevel;
        var instrIdx = vi / levelsSlots;
        var remainder = vi % levelsSlots;
        var priceLevel = remainder / _config.OrderSlotsPerLevel;
        var orderSlot = remainder % _config.OrderSlotsPerLevel;

        var instrumentId = _instrumentIndex.FirstOrDefault(kv => kv.Value == instrIdx).Key
            ?? throw new ArgumentException($"No instrument found for VI {vi}");

        return (instrumentId, priceLevel, orderSlot);
    }

    /// <summary>
    /// Get the number of registered instruments.
    /// </summary>
    public int RegisteredInstrumentCount => _instrumentIndex.Count;

    /// <summary>
    /// Check if an instrument is registered.
    /// </summary>
    public bool IsRegistered(string instrumentId) => _instrumentIndex.ContainsKey(instrumentId);
}
