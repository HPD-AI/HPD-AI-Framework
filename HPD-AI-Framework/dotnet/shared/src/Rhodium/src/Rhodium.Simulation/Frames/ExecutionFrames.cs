using HPD.Events;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Frames;

public readonly record struct ExecutionFillFrame(
    int StrategyIndex,
    int VariantId,
    int InstrumentIndex,
    long ClientOrderId,
    long VenueOrderId,
    long ExecutionId,
    Side Side,
    long FillPriceTicks,
    long FillQuantityLots,
    long FeeAmountScaled,
    int FeeCurrencyId,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<ExecutionFillFrame>
{
    public EventKind Kind => EventKind.Content;

    public ExecutionFillFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}
