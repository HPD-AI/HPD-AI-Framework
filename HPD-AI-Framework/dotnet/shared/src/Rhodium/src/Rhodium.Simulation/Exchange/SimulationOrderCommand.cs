using Rhodium.Primitives;

namespace Rhodium.Simulation.Exchange;

/// <summary>
/// Venue-routable command translated from a strategy order intent.
/// </summary>
public readonly record struct SimulationOrderCommand(
    StrategyId StrategyId,
    int VariantId,
    AssetId AssetId,
    Instrument Instrument,
    Venue Venue,
    OrderId ClientOrderId,
    Side Side,
    Qty Quantity,
    ExecutionSpec Execution,
    OrderListId? OrderListId = null,
    ContingencyType? ContingencyType = null,
    bool ReduceOnly = false,
    string? CorrelationId = null);

public readonly record struct PackageLegFill(
    Instrument Instrument,
    Side Side,
    Qty Quantity,
    Price Price);

public readonly record struct SimulationCancelCommand(
    StrategyId StrategyId,
    int VariantId,
    AssetId AssetId,
    Instrument Instrument,
    Venue Venue,
    OrderId OrderId,
    string? Reason = null);

public readonly record struct SimulationModifyCommand(
    StrategyId StrategyId,
    int VariantId,
    AssetId AssetId,
    Instrument Instrument,
    Venue Venue,
    OrderId OrderId,
    Qty? NewQuantity = null,
    Price? NewLimitPrice = null);
