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
/// Full normalized L2 order-book snapshot received.
/// </summary>
public sealed record BookSnapshotReceived(
    Instrument Instrument,
    Book Book
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Single order-book level delta received.
/// </summary>
public sealed record BookLevelDeltaReceived(
    Instrument Instrument,
    BookLevelDelta Delta
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Ordered batch of order-book level deltas received.
/// </summary>
public sealed record BookLevelDeltasReceived(
    Instrument Instrument,
    IReadOnlyList<BookLevelDelta> Deltas
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Individual external book order added to an L3 market-by-order book.
/// </summary>
public sealed record BookOrderAdded(
    Instrument Instrument,
    BookOrder Order,
    long VenueSequence = 0,
    byte Flags = 0
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Individual external book order modified in an L3 market-by-order book.
/// </summary>
public sealed record BookOrderModified(
    Instrument Instrument,
    BookOrder Order,
    long VenueSequence = 0,
    byte Flags = 0
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Individual external book order deleted from an L3 market-by-order book.
/// </summary>
public sealed record BookOrderDeleted(
    Instrument Instrument,
    BookOrderId OrderId,
    long VenueSequence = 0,
    byte Flags = 0
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Individual external book order executed in an L3 market-by-order book.
/// </summary>
public sealed record BookOrderExecuted(
    Instrument Instrument,
    BookOrderId OrderId,
    Qty ExecutedSize,
    long VenueSequence = 0,
    byte Flags = 0
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

/// <summary>
/// Fixed top-10 L2 order-book snapshot received.
/// </summary>
public sealed record BookDepth10Received(
    Instrument Instrument,
    IReadOnlyList<Level> Bids,
    IReadOnlyList<Level> Asks,
    long VenueSequence = 0,
    byte Flags = 0
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}
