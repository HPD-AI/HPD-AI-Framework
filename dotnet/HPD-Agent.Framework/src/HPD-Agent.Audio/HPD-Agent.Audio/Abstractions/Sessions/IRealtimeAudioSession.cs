using HPD.Agent.Audio.Policies;

namespace HPD.Agent.Audio.Sessions;

public interface IRealtimeAudioSession : IAsyncDisposable
{
    AudioSessionId Id { get; }

    RealtimeAudioSessionState State { get; }

    RealtimeAudioSessionSnapshot Snapshot { get; }

    ValueTask StartAsync(RealtimeAudioSessionOptions options, CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public sealed record RealtimeAudioSessionOptions
{
    public AudioPolicySet PolicySet { get; init; } = new();

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public sealed record RealtimeAudioSessionSnapshot
{
    public required AudioSessionId Id { get; init; }

    public required RealtimeAudioSessionState State { get; init; }
}

public enum RealtimeAudioSessionState
{
    Created = 0,
    Starting = 1,
    Active = 2,
    Draining = 3,
    Stopped = 4,
    Faulted = 5
}
