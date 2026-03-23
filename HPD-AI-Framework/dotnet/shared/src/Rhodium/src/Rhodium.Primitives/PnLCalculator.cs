namespace Rhodium.Primitives;

/// <summary>
/// Contract-aware PnL calculations.
/// </summary>
public static class PnLCalculator
{
    public static Money Calculate(
        ContractType type,
        Price entry,
        Price exit,
        Qty qty,
        decimal multiplier = 1m)
    {
        var pnl = type switch
        {
            ContractType.Spot =>
                (exit.Value - entry.Value) * qty.Value,

            ContractType.LinearPerp =>
                (exit.Value - entry.Value) * qty.Value * multiplier,

            ContractType.InversePerp =>
                entry.Value == 0 || exit.Value == 0
                    ? 0m
                    : ((1m / entry.Value) - (1m / exit.Value)) * qty.Value * multiplier,

            ContractType.Option =>
                throw new InvalidOperationException("Options require Greeks-based valuation"),

            _ => 0m
        };

        return new Money(pnl, exit.Currency);
    }
}
