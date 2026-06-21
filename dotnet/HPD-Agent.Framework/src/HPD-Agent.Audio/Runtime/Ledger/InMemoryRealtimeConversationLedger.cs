using HPD.Agent.Audio.Ledger;

namespace HPD.Agent.Audio.Runtime.Ledger;

public sealed class InMemoryRealtimeConversationLedger : IRealtimeConversationLedger
{
    private readonly List<RealtimeLedgerRecord> _records = [];
    private readonly object _gate = new();

    public bool FailNextAppend { get; set; }

    public ValueTask AppendAsync(RealtimeLedgerRecord record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailNextAppend)
        {
            FailNextAppend = false;
            throw new InvalidOperationException("Injected ledger append failure.");
        }

        lock (_gate)
        {
            _records.Add(record);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<RealtimeLedgerRecord> ReadAsync(
        LedgerQuery? query = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        RealtimeLedgerRecord[] snapshot;
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

    public ValueTask<LedgerSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RealtimeLedgerRecord[] snapshot;
        lock (_gate)
        {
            snapshot = _records.ToArray();
        }

        return ValueTask.FromResult(new LedgerSnapshot
        {
            SessionId = snapshot.FirstOrDefault()?.SessionId ?? new AudioSessionId("unknown-session"),
            Records = snapshot
        });
    }

    public IReadOnlyList<RealtimeLedgerRecord> ToArray()
    {
        lock (_gate)
        {
            return _records.ToArray();
        }
    }

    private static bool Matches(RealtimeLedgerRecord record, LedgerQuery? query)
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
