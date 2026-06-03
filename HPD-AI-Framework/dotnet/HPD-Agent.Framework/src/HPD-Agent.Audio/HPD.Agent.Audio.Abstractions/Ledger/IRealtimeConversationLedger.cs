namespace HPD.Agent.Audio.Ledger;

public interface IRealtimeConversationLedger
{
    ValueTask AppendAsync(RealtimeLedgerRecord record, CancellationToken cancellationToken = default);

    IAsyncEnumerable<RealtimeLedgerRecord> ReadAsync(
        LedgerQuery? query = null,
        CancellationToken cancellationToken = default);

    ValueTask<LedgerSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);
}

public sealed record LedgerQuery
{
    public AudioSessionId? SessionId { get; init; }

    public LedgerRecordFamily? Family { get; init; }
}

public sealed record LedgerSnapshot
{
    public required AudioSessionId SessionId { get; init; }

    public required IReadOnlyList<RealtimeLedgerRecord> Records { get; init; }
}
