using HPD.Agent.Audio.Media;
using HPD.Agent.Audio.Policies;

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

[Flags]
public enum TransportCapability
{
    None = 0,
    InputAudio = 1 << 0,
    OutputAudio = 1 << 1,
    InputControl = 1 << 2,
    OutputControl = 1 << 3,
    ClearOutputBuffer = 1 << 4,
    FiniteInput = 1 << 5,
    InputImage = 1 << 6,
    InputVideo = 1 << 7,
    InputDocument = 1 << 8,
    OutputImage = 1 << 9,
    OutputVideo = 1 << 10,
    OutputDocument = 1 << 11
}

public sealed record TransportBinding
{
    public required TransportBindingKind Kind { get; init; }

    public required AudioSessionId SessionId { get; init; }

    public InputContentRef? Content { get; init; }

    public BranchRef? Branch { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public enum TransportBindingKind
{
    ContentInput = 1,
    HostedAudioOutputWebSocket = 2,
    RealtimeWebRtc = 3,
    LiveKitRoom = 4,
    TelephonyMediaStream = 5
}

public sealed record AudioTransportContext
{
    public required AudioSessionId SessionId { get; init; }

    public BranchRef? Branch { get; init; }

    public required AudioPolicySet PolicySet { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record TransportOptions
{
    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public abstract record TransportCommand;

public sealed record StopTransportCommand(string Reason) : TransportCommand;

public sealed record ClearOutputBufferCommand(string Reason) : TransportCommand;

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
