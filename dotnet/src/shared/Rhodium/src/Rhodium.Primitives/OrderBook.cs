namespace Rhodium.Primitives;

/// <summary>
/// A single price level in the order book.
/// </summary>
public readonly record struct Level(Price Price, Qty Size, int OrderCount = 0);

/// <summary>
/// Order book snapshot.
/// </summary>
public sealed class Book
{
    public required Instrument Instrument { get; init; }
    public required Instant Time { get; init; }
    public required Level[] Bids { get; init; }
    public required Level[] Asks { get; init; }

    // Best bid/ask
    public Level? BestBid => Bids.Length > 0 ? Bids[0] : null;
    public Level? BestAsk => Asks.Length > 0 ? Asks[0] : null;

    public Price? Bid => BestBid?.Price;
    public Price? Ask => BestAsk?.Price;
    public Price? Mid => Bid.HasValue && Ask.HasValue
        ? new((Bid.Value.Value + Ask.Value.Value) / 2m)
        : null;
    public Price? Spread => Bid.HasValue && Ask.HasValue
        ? Ask.Value - Bid.Value
        : null;

    // Depth
    public Qty BidDepth(int levels = int.MaxValue) =>
        new(Bids.Take(levels).Sum(l => l.Size.Value));

    public Qty AskDepth(int levels = int.MaxValue) =>
        new(Asks.Take(levels).Sum(l => l.Size.Value));

    // Imbalance (positive = more bids, negative = more asks)
    public decimal Imbalance(int levels = 1)
    {
        var bidQty = BidDepth(levels).Value;
        var askQty = AskDepth(levels).Value;
        var total = bidQty + askQty;
        return total > 0 ? (bidQty - askQty) / total : 0m;
    }

    // VWAP to fill a given quantity
    public Price? VwapToFill(Side side, Qty qty)
    {
        var levels = side == Side.Buy ? Asks : Bids;
        var remaining = qty.Value;
        var cost = 0m;

        foreach (var level in levels)
        {
            var fillQty = Math.Min(remaining, level.Size.Value);
            cost += fillQty * level.Price.Value;
            remaining -= fillQty;
            if (remaining <= 0) break;
        }

        return remaining <= 0 ? new Price(cost / qty.Value) : null;
    }

    public static Book Empty(Instrument instrument, Instant time) => new()
    {
        Instrument = instrument,
        Time = time,
        Bids = [],
        Asks = []
    };
}
