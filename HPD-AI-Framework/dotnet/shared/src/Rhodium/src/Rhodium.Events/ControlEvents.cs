namespace Rhodium.Events;

// ==================== CONTROL EVENTS ====================

/// <summary>
/// User requested cancellation of all orders/positions.
/// Highest priority - jumps queue in live trading.
/// </summary>
public sealed record UserCancellation(
    string? Reason = null
) : ControlEvent;

/// <summary>
/// Risk limit was breached.
/// Highest priority - triggers immediate liquidation.
/// </summary>
public sealed record RiskLimitBreached(
    string LimitName,
    decimal CurrentValue,
    decimal LimitValue
) : ControlEvent;

/// <summary>
/// Emergency stop requested.
/// </summary>
public sealed record EmergencyStop(
    string Reason
) : ControlEvent;
