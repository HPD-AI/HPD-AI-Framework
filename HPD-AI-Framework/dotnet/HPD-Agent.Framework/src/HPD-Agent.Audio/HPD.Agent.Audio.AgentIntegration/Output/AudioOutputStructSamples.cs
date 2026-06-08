using HPD.Events;
using HPD.Events.Struct;

namespace HPD.Agent.Audio.AgentIntegration.Output;

public readonly record struct AudioOutputPlayoutSample(
    string SessionId,
    string OutputFlowId,
    string? SegmentId,
    int SegmentIndex,
    long PlayedUntilNs,
    int PlayedTextLength,
    long TimestampNs,
    long SequenceNumber = 0) :
    AgentStructEvent,
    ISequencedStructEvent<AudioOutputPlayoutSample>
{
    public EventKind Kind => EventKind.Diagnostic;

    public AudioOutputPlayoutSample WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct AudioOutputQueueDepthSample(
    string SessionId,
    string OutputFlowId,
    int QueuedSegments,
    int QueuedFrames,
    long QueuedDurationNs,
    long TimestampNs,
    long SequenceNumber = 0) :
    AgentStructEvent,
    ISequencedStructEvent<AudioOutputQueueDepthSample>
{
    public EventKind Kind => EventKind.Diagnostic;

    public AudioOutputQueueDepthSample WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

public readonly record struct AudioOutputUnderrunSample(
    string SessionId,
    string OutputFlowId,
    string? SegmentId,
    int SegmentIndex,
    long UnderrunDurationNs,
    long TimestampNs,
    long SequenceNumber = 0) :
    AgentStructEvent,
    ISequencedStructEvent<AudioOutputUnderrunSample>
{
    public EventKind Kind => EventKind.Diagnostic;

    public AudioOutputUnderrunSample WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}
