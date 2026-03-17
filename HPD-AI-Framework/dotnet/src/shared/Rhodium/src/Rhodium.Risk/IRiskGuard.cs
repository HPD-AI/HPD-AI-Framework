using Rhodium.Primitives;

namespace Rhodium.Risk;

/// <summary>
/// Risk guard interface for order validation.
/// </summary>
public interface IRiskGuard
{
    /// <summary>
    /// Checks if an order should be approved or refused based on risk constraints.
    /// </summary>
    RiskDecision<SubmitOrder> CheckOrder(SubmitOrder order, IAnalyzer analyzer);
}

/// <summary>
/// Analyzer interface providing portfolio metrics for risk checks.
/// </summary>
public interface IAnalyzer
{
    /// <summary>
    /// Total equity in the portfolio.
    /// </summary>
    Money TotalEquity { get; }
}
