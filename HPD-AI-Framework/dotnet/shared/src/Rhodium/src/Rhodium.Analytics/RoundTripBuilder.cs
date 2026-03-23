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
        var fillsByInstrument = fills
            .GroupBy(f => f.Instrument)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.Time).ToList());

        foreach (var (instrument, instrumentFills) in fillsByInstrument)
        {
            foreach (var roundTrip in MatchFifo(instrument, instrumentFills))
            {
                yield return roundTrip;
            }
        }
    }

    private static IEnumerable<RoundTrip> MatchFifo(
        Instrument instrument,
        List<OrderFilled> fills)
    {
        var openPositions = new Queue<(OrderFilled Fill, decimal Remaining)>();

        foreach (var fill in fills)
        {
            var remaining = fill.FilledQty.Value;

            // Try to match against existing positions (opposite side)
            while (remaining > 0 && openPositions.Count > 0)
            {
                var (entryFill, entryRemaining) = openPositions.Peek();

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

                // Remove fully matched entry
                openPositions.Dequeue();

                // Re-add if partially matched
                if (newEntryRemaining > 0)
                {
                    // Create new queue with updated entry at front
                    var temp = openPositions.ToList();
                    openPositions.Clear();
                    openPositions.Enqueue((entryFill, newEntryRemaining));
                    foreach (var item in temp)
                        openPositions.Enqueue(item);
                }
            }

            // Add remaining as new open position
            if (remaining > 0)
                openPositions.Enqueue((fill, remaining));
        }
    }

    /// <summary>
    /// Build from order history (virtual index arrays).
    /// </summary>
    public static IEnumerable<RoundTrip> FromOrders(IEnumerable<Order> orders)
    {
        var fills = orders
            .Where(o => o.FilledQty > Qty.Zero)
            .Select(o => new OrderFilled(
                o.Id,
                o.Instrument,
                o.VariantId,
                o.Side,
                o.FilledQty,
                o.AvgFillPrice ?? Price.Zero,
                o.TotalCommission)
            {
                // Use the response timestamp as fill time
                Timestamp = o.ResponseTimestamp.ToDateTimeOffset()
            });

        return FromFills(fills);
    }
}
