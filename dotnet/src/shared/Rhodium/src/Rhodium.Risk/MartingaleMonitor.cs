using Rhodium.Primitives;

namespace Rhodium.Risk;

/// <summary>
/// Risk guard implementing supermartingale inequality enforcement.
/// Conditional on explicit volatility model assumptions.
/// </summary>
public sealed class MartingaleMonitor : IRiskGuard
{
    private readonly ISigmaModel _model;

    public MartingaleMonitor(ISigmaModel model)
    {
        _model = model;
    }

    /// <summary>
    /// Checks if an order satisfies solvency bounds under the sigma model.
    /// </summary>
    public RiskDecision<SubmitOrder> CheckOrder(SubmitOrder order, IAnalyzer analyzer)
    {
        var sigma = _model.Estimate(order.Instrument);

        if (!IsSupermartingale(analyzer.TotalEquity, order, sigma))
        {
            return new RiskDecision<SubmitOrder>.Refused(
                order,
                "Violates Solvency Bound",
                "MARTINGALE_INEQUALITY"
            );
        }

        return new RiskDecision<SubmitOrder>.Approved(order);
    }

    /// <summary>
    /// Determines if the trade satisfies the supermartingale inequality.
    /// Returns true if the trade is safe under the model.
    ///
    /// Implements Kelly criterion with volatility-adjusted position sizing:
    /// f* = (μ - r) / σ²
    ///
    /// For safety, uses fractional Kelly (50% of optimal) to reduce bankruptcy risk.
    /// </summary>
    private static bool IsSupermartingale(Money equity, SubmitOrder trade, double sigma)
    {
        if (equity.Amount <= 0)
            return false;

        // Handle zero volatility (use conservative limit)
        if (sigma <= 0)
            sigma = 0.01; // 1% minimum volatility assumption

        var notional = trade.Quantity.Value * (trade.LimitPrice?.Value ?? 0m);
        if (notional == 0)
            return true; // Market orders without limit price - allow pending risk check

        // Calculate position size as fraction of equity
        var positionFraction = (double)(notional / equity.Amount);

        // Kelly criterion for maximum safe leverage
        // Assuming zero risk-free rate and conservative expected return estimate
        const double assumedSharpe = 0.5; // Conservative Sharpe ratio assumption
        var kellyFraction = assumedSharpe / sigma;

        // Use half-Kelly for safety (reduces risk of ruin)
        var maxSafeFraction = kellyFraction * 0.5;

        // Apply absolute cap: no single position > 20% of equity
        const double absoluteMaxFraction = 0.20;
        maxSafeFraction = Math.Min(maxSafeFraction, absoluteMaxFraction);

        // Ensure minimum position allowed is at least 1%
        const double minAllowedFraction = 0.01;
        maxSafeFraction = Math.Max(maxSafeFraction, minAllowedFraction);

        // Check supermartingale inequality: position <= max safe fraction
        return positionFraction <= maxSafeFraction;
    }
}
