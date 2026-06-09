using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Graph;

public interface IDirectionalBoundedFrameGraph : IAsyncDisposable
{
    ValueTask StartAsync(AudioGraphOptions options, CancellationToken cancellationToken = default);

    ValueTask PublishAsync(AudioGraphFrame frame, CancellationToken cancellationToken = default);

    IAsyncEnumerable<AudioGraphObservation> ReadObservationsAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public sealed record AudioGraphOptions
{
    public int? Capacity { get; init; }
}

public sealed record AudioGraphFrame
{
    public required AudioSessionId SessionId { get; init; }

    public required AudioGraphLane Lane { get; init; }

    public CanonicalMediaEnvelope? Media { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record AudioGraphObservation
{
    public required AudioSessionId SessionId { get; init; }

    public required string Kind { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }
}

public enum AudioGraphLane
{
    InboundMedia = 0,
    ProviderUpdates = 1,
    Output = 2,
    Control = 3
}
