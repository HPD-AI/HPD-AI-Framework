using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Transports;

public enum TransportAdapterState
{
    Created = 0,
    Starting = 1,
    Active = 2,
    Draining = 3,
    Stopped = 4,
    Faulted = 5
}

public abstract record TransportEvent
{
    public required TransportAdapterId AdapterId { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public sealed record TransportStateChangedEvent : TransportEvent
{
    public required TransportAdapterState State { get; init; }
}

public sealed record TransportMediaReceivedEvent : TransportEvent
{
    public required CanonicalMediaEnvelope Envelope { get; init; }
}

public sealed record TransportErrorEvent : TransportEvent
{
    public required AudioErrorInfo Error { get; init; }
}
