using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Account-slice notional basis used to convert financing rates into replay cash-flow commands.
/// </summary>
public readonly record struct FinancingRateBasis(
    StrategyId StrategyId,
    int VariantId,
    Currency Currency,
    Money BaseAmount,
    Instrument? Instrument = null,
    Qty Quantity = default)
{
    public static FinancingRateBasis FromPosition(CustodyPositionSnapshot position)
        => new(
            position.StrategyId,
            position.VariantId,
            position.MarketValue.Currency,
            new Money(Math.Abs(position.MarketValue.Amount), position.MarketValue.Currency),
            position.Instrument,
            position.Quantity);
}
