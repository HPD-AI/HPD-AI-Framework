using HPD.Agent.Audio.Trace;

namespace HPD.Agent.Audio.Runtime.Trace;

internal sealed class InMemoryAudioTraceProjectionV1
{
    private readonly List<RealtimeAudioTraceRecord> _records = [];
    private readonly object _gate = new();

    internal bool FailNextAppend { get; set; }

    internal ValueTask AppendAsync(RealtimeAudioTraceRecord record, CancellationToken cancellationToken = default)
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

    internal IReadOnlyList<RealtimeAudioTraceRecord> ToArray()
    {
        lock (_gate)
        {
            return _records.ToArray();
        }
    }

}
