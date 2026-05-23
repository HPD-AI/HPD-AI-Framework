using Rhodium.Primitives;

namespace Rhodium.Events;

// ==================== MARKET EVENTS ====================

/// <summary>
/// A new quote was received.
/// </summary>
public sealed record QuoteReceived(
    Instrument Instrument,
    Quote Quote
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// A trade occurred in the market.
/// </summary>
public sealed record TradeOccurred(
    Instrument Instrument,
    Trade Trade
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// A bar closed.
/// </summary>
public sealed record BarClosed(
    Instrument Instrument,
    Bar Bar
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Order book update.
/// </summary>
public sealed record BookUpdated(
    Instrument Instrument,
    Book Book
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Single order-book level delta received.
/// </summary>
public sealed record BookDeltaReceived(
    Instrument Instrument,
    BookDelta Delta
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Ordered batch of order-book level deltas received.
/// </summary>
public sealed record BookDeltasReceived(
    Instrument Instrument,
    IReadOnlyList<BookDelta> Deltas
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Fixed-depth top-N order-book snapshot received.
/// </summary>
public sealed record BookDepthSnapshotReceived(
    Instrument Instrument,
    IReadOnlyList<Level> Bids,
    IReadOnlyList<Level> Asks,
    int Depth,
    long VenueSequence = 0,
    byte Flags = 0
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}
