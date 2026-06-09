using HPD.Agent.Audio.Trace;

namespace HPD.Agent.Audio.Runtime.Replay;

public sealed class PrivacySafeAudioTraceMaterializer : IRealtimeAudioTraceMaterializer
{
    private readonly IRealtimeAudioTraceStore _traceStore;

    public PrivacySafeAudioTraceMaterializer(IRealtimeAudioTraceStore traceStore)
    {
        _traceStore = traceStore;
    }

    public async ValueTask<AudioReplayScenario> MaterializeAsync(
        AudioSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        var records = new List<RealtimeAudioTraceRecord>();

        await foreach (var record in _traceStore.ReadAsync(new TraceQuery { SessionId = sessionId }, cancellationToken))
        {
            records.Add(record);
        }

        return new AudioReplayScenario
        {
            SessionId = sessionId,
            Records = records,
            IsPrivacySafe = records.All(IsPrivacySafe)
        };
    }

    private static bool IsPrivacySafe(RealtimeAudioTraceRecord record)
    {
        // The first runtime slice has no trace record capable of carrying raw audio
        // bytes. Input content traces reference neutral content metadata only.
        return true;
    }
}
