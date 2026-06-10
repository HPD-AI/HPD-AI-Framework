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

    public PositionValuationInput ToValuationInput() =>
        new(Instrument, Quantity, AvgEntryPrice, RealizedPnL);

    public PositionValuation Value(
        InstrumentContract contract,
        Price mark,
        IInstrumentValuationModel? valuation = null)
    {
        valuation ??= DefaultInstrumentValuationModel.Instance;
        return valuation.ValuePosition(contract, ToValuationInput(), mark);
    }

    // Apply a fill to this position
    public void ApplyFill(InstrumentContract contract, Side side, Qty qty, Price price, Money commission)
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
            var pnl = DefaultInstrumentValuationModel.Instance.RealizedPnL(
                contract,
                new Qty(closingQty * (Quantity.IsPositive ? 1m : -1m)),
                AvgEntryPrice,
                price);
            Quantity = new Qty(newQty);
            RealizedPnL = RealizedPnL + pnl - commission;
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
