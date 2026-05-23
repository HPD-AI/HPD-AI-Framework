namespace Rhodium.Primitives;

/// <summary>
/// A position in an instrument.
/// </summary>
public sealed class Position
{
    public Instrument Instrument { get; internal set; } = Instrument.Unknown;
    public Qty Quantity { get; internal set; }
    public Price AvgEntryPrice { get; internal set; }
    public Money RealizedPnL { get; internal set; }
    public Instant OpenedAt { get; internal set; }
    public Instant? ClosedAt { get; internal set; }

    // Derived
    public bool IsFlat => Quantity.IsZero;
    public bool IsLong => Quantity.IsPositive;
    public bool IsShort => Quantity.IsNegative;
    public Side Side => SideExtensions.FromQty(Quantity);

    public Money CostBasis => new(Quantity.Abs.Value * AvgEntryPrice.Value, AvgEntryPrice.Currency);

    public Money UnrealizedPnL(Price currentPrice)
    {
        var pnl = (currentPrice.Value - AvgEntryPrice.Value) * Quantity.Value;
        return new Money(pnl, currentPrice.Currency);
    }

    public Money TotalPnL(Price currentPrice) => RealizedPnL + UnrealizedPnL(currentPrice);

    public decimal UnrealizedPnLPercent(Price currentPrice) =>
        AvgEntryPrice.Value != 0
            ? (currentPrice.Value - AvgEntryPrice.Value) / AvgEntryPrice.Value * (Quantity.IsPositive ? 1 : -1)
            : 0m;

    // Apply a fill to this position
    public void ApplyFill(Side side, Qty qty, Price price, Money commission)
    {
        var fillSign = side == Side.Buy ? 1m : -1m;
        var fillQty = qty.Value * fillSign;
        var newQty = Quantity.Value + fillQty;

        var isAdding = (Quantity.Value >= 0 && fillSign > 0) || (Quantity.Value <= 0 && fillSign < 0);

        if (isAdding || Quantity.IsZero)
        {
            // Adding to position - update average price
            var totalCost = Quantity.Abs.Value * AvgEntryPrice.Value + qty.Value * price.Value;
            AvgEntryPrice = Math.Abs(newQty) > 0
                ? new Price(totalCost / Math.Abs(newQty), price.Currency)
                : Price.Zero;
            Quantity = new Qty(newQty);
            RealizedPnL = RealizedPnL - commission;
        }
        else
        {
            // Reducing position - realize P&L
            var closingQty = Math.Min(qty.Value, Quantity.Abs.Value);
            var pnl = (price.Value - AvgEntryPrice.Value) * closingQty * (Quantity.IsPositive ? 1 : -1);
            Quantity = new Qty(newQty);
            RealizedPnL = RealizedPnL + new Money(pnl, price.Currency) - commission;
            if (Math.Abs(newQty) < 0.0000001m)
                ClosedAt = Instant.Now;
        }
    }

    public void ApplyTransfer(Qty quantityDelta, Price carryingPrice)
    {
        if (quantityDelta.IsZero)
            return;

        var newQty = Quantity.Value + quantityDelta.Value;
        var isAddingToSameSide = Quantity.IsZero
            || Math.Sign(Quantity.Value) == Math.Sign(quantityDelta.Value);

        if (isAddingToSameSide)
        {
            var totalCost = Quantity.Abs.Value * AvgEntryPrice.Value
                + quantityDelta.Abs.Value * carryingPrice.Value;
            AvgEntryPrice = Math.Abs(newQty) > 0m
                ? new Price(totalCost / Math.Abs(newQty), carryingPrice.Currency)
                : Price.Zero;
        }

        Quantity = new Qty(newQty);
        if (Quantity.IsZero)
        {
            AvgEntryPrice = Price.Zero;
            ClosedAt = Instant.Now;
        }
        else if (OpenedAt == default)
        {
            OpenedAt = Instant.Now;
        }
    }

    public void ApplySplit(decimal splitRatio)
    {
        if (splitRatio <= 0m)
            throw new ArgumentOutOfRangeException(nameof(splitRatio), "Split ratio must be positive.");

        if (Quantity.IsZero)
            return;

        Quantity = new Qty(Quantity.Value * splitRatio);
        if (AvgEntryPrice.Value != 0m)
            AvgEntryPrice = new Price(AvgEntryPrice.Value / splitRatio, AvgEntryPrice.Currency);
    }

    public static Position Empty(Instrument instrument) => new()
    {
        Instrument = instrument,
        Quantity = Qty.Zero,
        AvgEntryPrice = Price.Zero,
        RealizedPnL = Money.Zero(Currency.USD),
        OpenedAt = Instant.Now
    };

    internal void ResetSnapshot(
        Instrument instrument,
        Qty quantity,
        Price avgEntryPrice,
        Money realizedPnL,
        Instant openedAt)
    {
        Instrument = instrument;
        Quantity = quantity;
        AvgEntryPrice = avgEntryPrice;
        RealizedPnL = realizedPnL;
        OpenedAt = openedAt;
        ClosedAt = null;
    }
}
