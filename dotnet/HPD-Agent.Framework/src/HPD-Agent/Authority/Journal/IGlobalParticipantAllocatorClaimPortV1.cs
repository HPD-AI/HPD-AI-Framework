namespace HPD.Agent.Authority;

internal sealed record GlobalParticipantAllocatorClaimRequestV1
{
    private readonly byte[] _bytes;
    internal GlobalParticipantAllocatorClaimRequestV1(GlobalParticipantAllocatorJournalId journalId, GlobalParticipantAuthorityHeadV1? expectedHead, ReadOnlyMemory<byte> exactCanonicalRecordBytes, JournalFactId factId)
    { JournalId=journalId;ExpectedHead=expectedHead;_bytes=exactCanonicalRecordBytes.ToArray();ExactCanonicalRecordBytes=_bytes;FactId=factId; }
    internal GlobalParticipantAllocatorJournalId JournalId { get; }
    internal GlobalParticipantAuthorityHeadV1? ExpectedHead { get; }
    internal ReadOnlyMemory<byte> ExactCanonicalRecordBytes { get; }
    internal JournalFactId FactId { get; }
}

internal abstract record GlobalParticipantAllocatorClaimResultV1
{
    private GlobalParticipantAllocatorClaimResultV1() { }
    internal sealed record Committed(GlobalParticipantAuthorityHeadV1 Head, ulong Sequence, ReadOnlyMemory<byte> ExactCanonicalRecordBytes) : GlobalParticipantAllocatorClaimResultV1;
    internal sealed record AlreadyCommitted(GlobalParticipantAuthorityHeadV1 Head, ulong Sequence, ReadOnlyMemory<byte> ExactCanonicalRecordBytes) : GlobalParticipantAllocatorClaimResultV1;
    internal sealed record HeadConflict(GlobalParticipantAuthorityHeadV1? CurrentHead) : GlobalParticipantAllocatorClaimResultV1;
    internal sealed record InvalidRecord(BoundedAscii SafeCode) : GlobalParticipantAllocatorClaimResultV1;
    internal sealed record LifetimeExhausted(ulong RecordCount, ulong TotalCanonicalRecordBytes) : GlobalParticipantAllocatorClaimResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GlobalParticipantAllocatorClaimResultV1;
}

internal abstract record GlobalParticipantAllocatorSnapshotResultV1
{
    private GlobalParticipantAllocatorSnapshotResultV1() { }
    internal sealed record Current(GlobalParticipantAllocatorExactRecordSnapshotV1 Snapshot) : GlobalParticipantAllocatorSnapshotResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GlobalParticipantAllocatorSnapshotResultV1;
}

internal interface IGlobalParticipantAllocatorClaimPortV1
{ ValueTask<GlobalParticipantAllocatorClaimResultV1> ClaimAsync(GlobalParticipantAllocatorClaimRequestV1 request, CancellationToken cancellationToken); }

internal sealed record GlobalParticipantAllocatorExactRecordSnapshotV1
{
    private readonly ReadOnlyMemory<byte>[] _records;
    internal GlobalParticipantAllocatorExactRecordSnapshotV1(GlobalParticipantAllocatorJournalId journalId,GlobalParticipantAuthorityHeadV1? head,ulong recordCount,ulong totalCanonicalRecordBytes,IReadOnlyList<ReadOnlyMemory<byte>> exactCanonicalRecords)
    { JournalId=journalId;Head=head;RecordCount=recordCount;TotalCanonicalRecordBytes=totalCanonicalRecordBytes;_records=exactCanonicalRecords.Select(x=>(ReadOnlyMemory<byte>)x.ToArray()).ToArray();ExactCanonicalRecords=Array.AsReadOnly(_records); }
    internal GlobalParticipantAllocatorJournalId JournalId { get; }
    internal GlobalParticipantAuthorityHeadV1? Head { get; }
    internal ulong RecordCount { get; }
    internal ulong TotalCanonicalRecordBytes { get; }
    internal IReadOnlyList<ReadOnlyMemory<byte>> ExactCanonicalRecords { get; }
}

internal interface IGlobalParticipantAllocatorExactRecordSnapshotReaderV1
{ GlobalParticipantAllocatorSnapshotResultV1 ReadExactSnapshot(); }
