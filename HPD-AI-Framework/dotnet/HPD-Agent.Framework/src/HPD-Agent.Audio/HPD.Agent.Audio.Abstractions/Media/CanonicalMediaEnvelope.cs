namespace HPD.Agent.Audio.Media;

public sealed record CanonicalMediaEnvelope
{
    public required AudioSessionId SessionId { get; init; }

    public required MediaKind Kind { get; init; }

    public required MediaDirection Direction { get; init; }

    public required MediaPayloadRef Payload { get; init; }

    public required MediaFormatDescriptor Format { get; init; }

    public MediaTimeline? Timeline { get; init; }

    public MediaCaptureDisposition CaptureDisposition { get; init; } = MediaCaptureDisposition.MetadataOnly;

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public enum MediaKind
{
    Audio = 0,
    Image = 1,
    Video = 2,
    Document = 3,
    Text = 4,
    Control = 5,
    Unknown = 6
}

public enum MediaDirection
{
    Inbound = 0,
    Outbound = 1,
    Internal = 2
}

public enum MediaCaptureDisposition
{
    NotCaptured = 0,
    MetadataOnly = 1,
    DigestOnly = 2,
    ArtifactRef = 3,
    RawRetained = 4
}
