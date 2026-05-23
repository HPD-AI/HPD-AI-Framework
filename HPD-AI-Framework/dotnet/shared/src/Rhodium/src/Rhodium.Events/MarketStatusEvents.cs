using Rhodium.Primitives;

namespace Rhodium.Events;

/// <summary>
/// Venue trading status changed during replay.
/// </summary>
public sealed record VenueStatusChanged(
    Venue Venue,
    MarketStatus Status,
    string? Reason = null
) : FinanceEvent;

/// <summary>
/// Instrument trading status changed during replay.
/// </summary>
public sealed record InstrumentStatusChanged(
    Instrument Instrument,
    MarketStatus Status,
    string? Reason = null
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}

/// <summary>
/// Instrument closed with a close or settlement mark.
/// </summary>
public sealed record InstrumentClosed(
    Instrument Instrument,
    Price ClosePrice,
    string? Reason = null
) : MarketEvent
{
    public override Instrument Instrument { get; } = Instrument;
}
