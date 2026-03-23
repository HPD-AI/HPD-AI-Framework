using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Operational risk guard - connector-level firewall.
/// Checks orders BEFORE submission (fat-finger protection).
/// </summary>
public interface IRiskGuard
{
    /// <summary>
    /// Check if order passes operational risk limits.
    /// </summary>
    /// <param name="order">Order to check</param>
    /// <param name="currentPrice">Current market price (for deviation check)</param>
    /// <param name="currentPosition">Current position (for position limit check)</param>
    /// <returns>Risk decision (approved or refused with reason)</returns>
    RiskDecision Check(SubmitOrder order, Price? currentPrice, decimal currentPosition);
}

/// <summary>
/// Default risk guard with configurable limits.
/// </summary>
public sealed class DefaultRiskGuard : IRiskGuard
{
    /// <summary>Maximum notional value per order.</summary>
    public Money MaxNotional { get; init; } = new(1_000_000m, Currency.USD);

    /// <summary>Maximum price deviation from current market (e.g., 0.10 = 10%).</summary>
    public decimal MaxPriceDeviationPercent { get; init; } = 0.10m;

    /// <summary>Maximum order size in units.</summary>
    public decimal MaxOrderSize { get; init; } = 10_000m;

    /// <summary>Maximum position size (absolute value).</summary>
    public decimal MaxPositionSize { get; init; } = 100_000m;

    public RiskDecision Check(SubmitOrder order, Price? currentPrice, decimal currentPosition)
    {
        // Check max order size
        if (order.Quantity.Value > MaxOrderSize)
            return RiskDecision.Refused(
                $"Order size {order.Quantity} exceeds max {MaxOrderSize}",
                RiskCode.MaxSize);

        // Check position limit
        var newPosition = order.Side == Side.Buy
            ? currentPosition + order.Quantity.Value
            : currentPosition - order.Quantity.Value;
        if (Math.Abs(newPosition) > MaxPositionSize)
            return RiskDecision.Refused(
                $"New position {newPosition} would exceed max {MaxPositionSize}",
                RiskCode.MaxPosition);

        // Check notional value
        var price = order.LimitPrice ?? currentPrice ?? Price.Zero;
        var notional = order.Quantity.Value * price.Value;
        if (notional > MaxNotional.Amount)
            return RiskDecision.Refused(
                $"Notional {notional} exceeds max {MaxNotional.Amount}",
                RiskCode.MaxNotional);

        // Check price deviation
        if (currentPrice.HasValue && order.LimitPrice.HasValue)
        {
            var deviation = Math.Abs(order.LimitPrice.Value.Value - currentPrice.Value.Value)
                          / currentPrice.Value.Value;
            if (deviation > MaxPriceDeviationPercent)
                return RiskDecision.Refused(
                    $"Price deviation {deviation:P2} exceeds max {MaxPriceDeviationPercent:P2}",
                    RiskCode.PriceBand);
        }

        return RiskDecision.Approved();
    }
}

/// <summary>
/// Risk decision result.
/// </summary>
public readonly record struct RiskDecision(bool IsApproved, string? Reason, RiskCode Code)
{
    public static RiskDecision Approved() => new(true, null, RiskCode.None);
    public static RiskDecision Refused(string reason, RiskCode code) => new(false, reason, code);
}

/// <summary>
/// Risk rejection codes.
/// </summary>
public enum RiskCode : byte
{
    None = 0,
    MaxSize = 1,
    MaxPosition = 2,
    MaxNotional = 3,
    PriceBand = 4,
    RateLimit = 5,
    MarketClosed = 6,
    InstrumentHalted = 7
}
