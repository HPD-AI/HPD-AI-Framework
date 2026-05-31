using HPD.Events;
using HPD.Events.Struct;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Frames;

public readonly record struct BookOrderAddedFrame(
    int InstrumentIndex,
    long OrderId,
    Side Side,
    long PriceTicks,
    long SizeLots,
    long VenueSequence,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<BookOrderAddedFrame>
{
    public EventKind Kind => EventKind.Content;

    public BookOrderAddedFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct BookOrderModifiedFrame(
    int InstrumentIndex,
    long OrderId,
    Side Side,
    long PriceTicks,
    long SizeLots,
    long VenueSequence,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<BookOrderModifiedFrame>
{
    public EventKind Kind => EventKind.Content;

    public BookOrderModifiedFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct BookOrderDeletedFrame(
    int InstrumentIndex,
    long OrderId,
    long VenueSequence,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<BookOrderDeletedFrame>
{
    public EventKind Kind => EventKind.Content;

    public BookOrderDeletedFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct BookOrderExecutedFrame(
    int InstrumentIndex,
    long OrderId,
    long ExecutedLots,
    long VenueSequence,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<BookOrderExecutedFrame>
{
    public EventKind Kind => EventKind.Content;

    public BookOrderExecutedFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}
