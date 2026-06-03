namespace HPD.Agent.Audio.Trace;

public interface IRealtimeAudioTraceStore
{
    ValueTask AppendAsync(RealtimeAudioTraceRecord record, CancellationToken cancellationToken = default);

    IAsyncEnumerable<RealtimeAudioTraceRecord> ReadAsync(
        TraceQuery? query = null,
        CancellationToken cancellationToken = default);
}

public sealed record TraceQuery
{
    public AudioSessionId? SessionId { get; init; }

    public RealtimeAudioTraceRecordFamily? Family { get; init; }
}
