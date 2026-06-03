using HPD.Agent.Audio.Trace;

namespace HPD.Agent.Audio.Runtime.Trace;

public sealed class InMemoryRealtimeAudioTraceStore : IRealtimeAudioTraceStore
{
    private readonly List<RealtimeAudioTraceRecord> _records = [];
    private readonly object _gate = new();

    public bool FailNextAppend { get; set; }

    public ValueTask AppendAsync(RealtimeAudioTraceRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailNextAppend)
        {
            FailNextAppend = false;
            throw new InvalidOperationException("Injected trace append failure.");
        }

        lock (_gate)
        {
            _records.Add(record);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<RealtimeAudioTraceRecord> ReadAsync(
        TraceQuery? query = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RealtimeAudioTraceRecord[] snapshot;
        lock (_gate)
        {
            snapshot = _records.ToArray();
        }

        foreach (var record in snapshot.Where(record => Matches(record, query)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }

    public IReadOnlyList<RealtimeAudioTraceRecord> ToArray()
    {
        lock (_gate)
        {
            return _records.ToArray();
        }
    }

    private static bool Matches(RealtimeAudioTraceRecord record, TraceQuery? query)
    {
        if (query is null)
        {
            return true;
        }

        if (query.SessionId is { } sessionId && record.SessionId != sessionId)
        {
            return false;
        }

        if (query.Family is { } family && record.Family != family)
        {
            return false;
        }

        return true;
    }
}
