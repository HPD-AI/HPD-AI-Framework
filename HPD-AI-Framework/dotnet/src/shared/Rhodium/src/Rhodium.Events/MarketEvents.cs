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
