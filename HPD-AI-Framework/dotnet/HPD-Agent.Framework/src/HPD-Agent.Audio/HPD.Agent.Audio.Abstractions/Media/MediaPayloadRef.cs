namespace HPD.Agent.Audio.Media;

public abstract record MediaPayloadRef
{
    public sealed record DecodedAudio(DecodedAudioFrameRef Frame) : MediaPayloadRef;

    public sealed record EncodedAudio(EncodedAudioFrameRef Frame) : MediaPayloadRef;

    public sealed record RealtimeMedia(RealtimeMediaFrameRef Frame) : MediaPayloadRef;

    public sealed record InputContent(InputContentRef Content) : MediaPayloadRef;

    public sealed record ArtifactRange(AudioArtifactRef Artifact, TimeSpan Offset, TimeSpan? Duration) : MediaPayloadRef;

    public sealed record ProviderContent(ProviderMediaRef ProviderRef) : MediaPayloadRef;

    public sealed record ExternalMediaRef(string MediaType, string RefId) : MediaPayloadRef;

    public sealed record MetadataOnly(string? Digest, string Reason) : MediaPayloadRef;
}

public sealed record DecodedAudioFrameRef(string RuntimeType, string RefId, AudioFormatDescriptor Format);

public sealed record EncodedAudioFrameRef(string RuntimeType, string RefId, EncodedAudioFormatDescriptor Format);

public sealed record RealtimeMediaFrameRef(string RuntimeType, string RefId, string? TrackId);
