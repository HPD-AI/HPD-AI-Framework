using Rhodium.Primitives;

namespace Rhodium.Events;

// ==================== EXECUTION EVENTS ====================

/// <summary>
/// Order was acknowledged by venue.
/// </summary>
public sealed record OrderAccepted(
    OrderId OrderId,
    StrategyId StrategyId,
    int VariantId
) : ExecutionEvent;

/// <summary>
/// Order was modified by venue.
/// </summary>
public sealed record OrderModified(
    OrderId OrderId,
    StrategyId StrategyId,
    int VariantId,
    Qty? NewQuantity = null,
    Price? NewLimitPrice = null
) : ExecutionEvent;

/// <summary>
/// Order was rejected.
/// </summary>
public sealed record OrderRejected(
    OrderId OrderId,
    StrategyId StrategyId,
    int VariantId,
    string Reason
) : ExecutionEvent;

/// <summary>
/// Order was filled (partially or fully).
/// </summary>
public sealed record OrderFilled(
    OrderId OrderId,
    Instrument Instrument,
    int VariantId,
    StrategyId StrategyId,
    Side Side,
    Qty FilledQty,
    Price FillPrice,
    Money Commission
) : ExecutionEvent
{
    public Money Value => new(FilledQty.Value * FillPrice.Value, FillPrice.Currency);
}

/// <summary>
/// Order was cancelled.
/// </summary>
public sealed record OrderCancelled(
    OrderId OrderId,
    StrategyId StrategyId,
    int VariantId,
    Qty RemainingQty,
    string Reason
) : ExecutionEvent;

/// <summary>
/// Order expired.
/// </summary>
public sealed record OrderExpired(
    OrderId OrderId,
    StrategyId StrategyId,
    int VariantId
) : ExecutionEvent;
