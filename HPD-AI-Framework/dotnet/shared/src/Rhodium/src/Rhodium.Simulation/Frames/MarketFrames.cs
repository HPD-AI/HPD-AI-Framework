using HPD.Events;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Frames;

public readonly record struct QuoteFrame(
    int InstrumentIndex,
    long BidTicks,
    long AskTicks,
    long BidSizeLots,
    long AskSizeLots,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<QuoteFrame>
{
    public EventKind Kind => EventKind.Content;

    public QuoteFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct TradeFrame(
    int InstrumentIndex,
    long PriceTicks,
    long SizeLots,
    Side AggressorSide,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<TradeFrame>
{
    public EventKind Kind => EventKind.Content;

    public TradeFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct BookLevelDeltaFrame(
    int InstrumentIndex,
    Side Side,
    long PriceTicks,
    long SizeLots,
    BookAction Action,
    long VenueSequence,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<BookLevelDeltaFrame>
{
    public EventKind Kind => EventKind.Content;

    public BookLevelDeltaFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct BookDepthLevelFrame(
    int InstrumentIndex,
    int Depth,
    int LevelIndex,
    Side Side,
    long PriceTicks,
    long SizeLots,
    int OrderCount,
    long VenueSequence,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<BookDepthLevelFrame>
{
    public EventKind Kind => EventKind.Content;

    public BookDepthLevelFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}
