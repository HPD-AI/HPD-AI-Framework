using HPD.Agent.Audio.Ledger;

namespace HPD.Agent.Audio.Runtime.Ledger;

internal sealed class InMemoryConversationProjectionV1
{
    private readonly List<RealtimeLedgerRecord> _records = [];
    private readonly object _gate = new();

    internal bool FailNextAppend { get; set; }

    internal ValueTask AppendAsync(RealtimeLedgerRecord record, CancellationToken cancellationToken = default)
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

    internal IReadOnlyList<RealtimeLedgerRecord> ToArray()
    {
        lock (_gate)
        {
            return _records.ToArray();
        }
    }

}
