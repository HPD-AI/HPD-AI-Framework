namespace HPD.Agent.Audio.LiveKit;

/// <summary>LiveKit transport behavior fixed before a room connection is admitted.</summary>
public sealed record LiveKitTransportProviderConfig
{
    public int ParticipantTokenTtlSeconds { get; init; } = 300;
    public bool AutoSubscribe { get; init; } = true;
    public bool AdaptiveStream { get; init; } = true;
    public bool Dynacast { get; init; } = true;
    public bool CaptureNativeLogs { get; init; }
    public int CallbackCapacity { get; init; } = LiveKitFfiHost.MaximumCopiedCallbacks;
}
