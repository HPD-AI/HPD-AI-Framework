using HPD.Events;

namespace Rhodium.Simulation.Frames;

public readonly record struct RiskMetricFrame(
    int VenueId,
    int InstrumentIndex,
    int MetricId,
    long ValueScaled,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<RiskMetricFrame>
{
    public EventKind Kind => EventKind.Diagnostic;

    public RiskMetricFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct TensorProjectionFrame(
    int InstrumentIndex,
    int FieldId,
    long ValueScaled,
    long TimestampNs,
    long SequenceNumber = 0)
    : IStructEvent, ISequencedStructEvent<TensorProjectionFrame>
{
    public EventKind Kind => EventKind.Diagnostic;

    public TensorProjectionFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}
