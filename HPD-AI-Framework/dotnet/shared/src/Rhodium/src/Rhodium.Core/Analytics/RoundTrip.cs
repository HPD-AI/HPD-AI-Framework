using Rhodium.Primitives;

namespace Rhodium.Analytics;

/// <summary>
/// A completed trade (entry fill matched with exit fill).
/// The atomic unit of trade analysis.
/// </summary>
public readonly record struct RoundTrip(
    Instrument Instrument,
    Side Side,
    Qty Quantity,
    Price EntryPrice,
    Price ExitPrice,
    Instant EntryTime,
    Instant ExitTime,
    Money Commission
)
{
    public Money GrossPnL => new(
        (ExitPrice.Value - EntryPrice.Value) * Quantity.Value * Side.Sign(),
        EntryPrice.Currency);

    public Money NetPnL => new(GrossPnL.Amount - Commission.Amount, GrossPnL.Currency);

    public decimal ReturnPct => EntryPrice.Value > 0
        ? (ExitPrice.Value - EntryPrice.Value) / EntryPrice.Value * Side.Sign()
        : 0m;

    public Duration HoldingPeriod => ExitTime - EntryTime;

    public bool IsWin => NetPnL.Amount > 0;
    public bool IsLoss => NetPnL.Amount < 0;
    public bool IsBreakeven => NetPnL.Amount == 0;

    public Money Notional => new(EntryPrice.Value * Quantity.Value, EntryPrice.Currency);
}
