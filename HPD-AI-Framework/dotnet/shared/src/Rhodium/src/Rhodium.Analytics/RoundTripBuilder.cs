using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Analytics;

/// <summary>
/// Builds RoundTrips from order fills using FIFO matching.
/// </summary>
public static class RoundTripBuilder
{
    public static IEnumerable<RoundTrip> FromFills(IEnumerable<OrderFilled> fills)
    {
        ArgumentNullException.ThrowIfNull(fills);

        var orderedFills = new List<OrderFilled>();
        foreach (var fill in fills)
            orderedFills.Add(fill);

        orderedFills.Sort(CompareFills);

        var start = 0;
        while (start < orderedFills.Count)
        {
            var instrument = orderedFills[start].Instrument;
            var end = start + 1;
            while (end < orderedFills.Count && orderedFills[end].Instrument == instrument)
                end++;

            foreach (var roundTrip in MatchFifo(instrument, orderedFills, start, end))
                yield return roundTrip;

            start = end;
        }
    }

    private static IEnumerable<RoundTrip> MatchFifo(
        Instrument instrument,
        IReadOnlyList<OrderFilled> fills,
        int start,
        int end)
    {
        var openFills = new List<OrderFilled>();
        var openRemaining = new List<decimal>();
        var openIndex = 0;

        for (var i = start; i < end; i++)
        {
            var fill = fills[i];
            var remaining = fill.FilledQty.Value;

            // Try to match against existing positions (opposite side)
            while (remaining > 0 && openIndex < openFills.Count)
            {
                var entryFill = openFills[openIndex];
                var entryRemaining = openRemaining[openIndex];

                // Same side = adding to position, stop matching
                if (entryFill.Side == fill.Side)
                    break;

                var matchQty = Math.Min(remaining, entryRemaining);

                // Calculate proportional commission
                var entryCommPortion = entryFill.Commission.Amount * (matchQty / entryFill.FilledQty.Value);
                var exitCommPortion = fill.Commission.Amount * (matchQty / fill.FilledQty.Value);

                yield return new RoundTrip(
                    Instrument: instrument,
                    Side: entryFill.Side,
                    Quantity: new Qty(matchQty),
                    EntryPrice: entryFill.FillPrice,
                    ExitPrice: fill.FillPrice,
                    EntryTime: entryFill.Time,
                    ExitTime: fill.Time,
                    Commission: new Money(
                        entryCommPortion + exitCommPortion,
                        fill.Commission.Currency)
                );

                remaining -= matchQty;
                var newEntryRemaining = entryRemaining - matchQty;

                if (newEntryRemaining > 0)
                    openRemaining[openIndex] = newEntryRemaining;
                else
                    openIndex++;
            }

            // Add remaining as new open position
            if (remaining > 0)
            {
                openFills.Add(fill);
                openRemaining.Add(remaining);
            }
        }
    }

    /// <summary>
    /// Build from order history (virtual index arrays).
    /// </summary>
    public static IEnumerable<RoundTrip> FromOrders(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var fills = new List<OrderFilled>();
        foreach (var order in orders)
        {
            if (order.FilledQty <= Qty.Zero)
                continue;

            fills.Add(new OrderFilled(
                    order.Id,
                    order.Instrument,
                    order.VariantId,
                    new StrategyId(0),
                    order.Side,
                    order.FilledQty,
                    order.AvgFillPrice ?? Price.Zero,
                    order.TotalCommission)
                {
                    // Use the response timestamp as fill time
                    Timestamp = order.ResponseTimestamp.ToDateTimeOffset()
                });
        }

        return FromFills(fills);
    }

    private static int CompareFills(OrderFilled left, OrderFilled right)
    {
        var venue = string.CompareOrdinal(left.Instrument.Venue.Name, right.Instrument.Venue.Name);
        if (venue != 0)
            return venue;

        var symbol = string.CompareOrdinal(left.Instrument.Asset.Symbol, right.Instrument.Asset.Symbol);
        if (symbol != 0)
            return symbol;

        return left.Time.CompareTo(right.Time);
    }
}
