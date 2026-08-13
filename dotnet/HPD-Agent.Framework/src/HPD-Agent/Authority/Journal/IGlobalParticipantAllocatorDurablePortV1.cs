namespace HPD.Agent.Authority;

internal sealed record GlobalParticipantAllocatorDurableClaimRequestV1
{
    private readonly byte[] _bytes;
    internal GlobalParticipantAllocatorDurableClaimRequestV1(GlobalParticipantAllocatorRealmLeaseV1 realmLease, GlobalParticipantAuthorityHeadV1? expectedHead, ReadOnlyMemory<byte> exactCanonicalRecordBytes, JournalFactId factId)
    {
        RealmLease = realmLease ?? throw new ArgumentException("A realm lease is required.", nameof(realmLease));
        if (!factId.IsValid) throw new ArgumentException("A valid fact ID is required.", nameof(factId));
        ExpectedHead = expectedHead; _bytes = exactCanonicalRecordBytes.ToArray(); ExactCanonicalRecordBytes = _bytes; FactId = factId;
    }
    internal GlobalParticipantAllocatorRealmLeaseV1 RealmLease { get; }
    internal GlobalParticipantAuthorityHeadV1? ExpectedHead { get; }
    internal ReadOnlyMemory<byte> ExactCanonicalRecordBytes { get; }
    internal JournalFactId FactId { get; }
}

internal abstract record GlobalParticipantAllocatorDurableClaimResultV1
{
    private GlobalParticipantAllocatorDurableClaimResultV1() { }
    internal sealed record Committed : GlobalParticipantAllocatorDurableClaimResultV1
    {
        private readonly byte[] _bytes;
        internal Committed(GlobalParticipantAuthorityHeadV1 head, ulong sequence, ReadOnlyMemory<byte> exactCanonicalRecordBytes) { Head=head;Sequence=sequence;_bytes=exactCanonicalRecordBytes.ToArray();ExactCanonicalRecordBytes=_bytes; }
        internal GlobalParticipantAuthorityHeadV1 Head { get; } internal ulong Sequence { get; } internal ReadOnlyMemory<byte> ExactCanonicalRecordBytes { get; }
    }
    internal sealed record AlreadyCommitted : GlobalParticipantAllocatorDurableClaimResultV1
    {
        private readonly byte[] _bytes;
        internal AlreadyCommitted(GlobalParticipantAuthorityHeadV1 head, ulong sequence, ReadOnlyMemory<byte> exactCanonicalRecordBytes) { Head=head;Sequence=sequence;_bytes=exactCanonicalRecordBytes.ToArray();ExactCanonicalRecordBytes=_bytes; }
        internal GlobalParticipantAuthorityHeadV1 Head { get; } internal ulong Sequence { get; } internal ReadOnlyMemory<byte> ExactCanonicalRecordBytes { get; }
    }
    internal sealed record ContradictoryDuplicate(JournalFactId FactId, BoundedAscii SafeCode) : GlobalParticipantAllocatorDurableClaimResultV1;
    internal sealed record HeadConflict(GlobalParticipantAuthorityHeadV1? CurrentHead) : GlobalParticipantAllocatorDurableClaimResultV1;
    internal sealed record InvalidRecord(BoundedAscii SafeCode) : GlobalParticipantAllocatorDurableClaimResultV1;
    internal sealed record LifetimeExhausted(ulong RecordCount, ulong TotalCanonicalRecordBytes) : GlobalParticipantAllocatorDurableClaimResultV1;
    internal sealed record RealmFenced(ulong CurrentFenceEpoch) : GlobalParticipantAllocatorDurableClaimResultV1;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GlobalParticipantAllocatorDurableClaimResultV1;
    internal sealed record OutcomeUnknown(JournalFactId FactId, BoundedAscii SafeCode) : GlobalParticipantAllocatorDurableClaimResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GlobalParticipantAllocatorDurableClaimResultV1;
}

internal interface IGlobalParticipantAllocatorDurableClaimPortV1
{ ValueTask<GlobalParticipantAllocatorDurableClaimResultV1> ClaimAsync(GlobalParticipantAllocatorDurableClaimRequestV1 request, CancellationToken cancellationToken); }

internal sealed record GlobalParticipantAllocatorReconcileRequestV1
{
    internal GlobalParticipantAllocatorReconcileRequestV1(GlobalParticipantAllocatorRealmLeaseV1 realmLease, JournalFactId factId)
    { RealmLease = realmLease ?? throw new ArgumentException("A realm lease is required.", nameof(realmLease)); if (!factId.IsValid) throw new ArgumentException("A valid fact ID is required.", nameof(factId)); FactId = factId; }
    internal GlobalParticipantAllocatorRealmLeaseV1 RealmLease { get; }
    internal JournalFactId FactId { get; }
}

internal abstract record GlobalParticipantAllocatorReconcileResultV1
{
    private GlobalParticipantAllocatorReconcileResultV1() { }
    internal sealed record Committed : GlobalParticipantAllocatorReconcileResultV1
    {
        private readonly byte[] _bytes;
        internal Committed(GlobalParticipantAuthorityHeadV1 head, ulong sequence, ReadOnlyMemory<byte> exactCanonicalRecordBytes) { Head=head;Sequence=sequence;_bytes=exactCanonicalRecordBytes.ToArray();ExactCanonicalRecordBytes=_bytes; }
        internal GlobalParticipantAuthorityHeadV1 Head { get; } internal ulong Sequence { get; } internal ReadOnlyMemory<byte> ExactCanonicalRecordBytes { get; }
    }
    internal sealed record NotFound : GlobalParticipantAllocatorReconcileResultV1;
    internal sealed record RealmFenced(ulong CurrentFenceEpoch) : GlobalParticipantAllocatorReconcileResultV1;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GlobalParticipantAllocatorReconcileResultV1;
    internal sealed record OutcomeUnknown(JournalFactId FactId, BoundedAscii SafeCode) : GlobalParticipantAllocatorReconcileResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GlobalParticipantAllocatorReconcileResultV1;
}

internal interface IGlobalParticipantAllocatorReconciliationPortV1
{ ValueTask<GlobalParticipantAllocatorReconcileResultV1> ReconcileAsync(GlobalParticipantAllocatorReconcileRequestV1 request, CancellationToken cancellationToken); }

internal sealed record GlobalParticipantAllocatorDurableSnapshotRequestV1
{
    internal GlobalParticipantAllocatorDurableSnapshotRequestV1(GlobalParticipantAllocatorRealmLeaseV1 realmLease) => RealmLease = realmLease ?? throw new ArgumentException("A realm lease is required.", nameof(realmLease));
    internal GlobalParticipantAllocatorRealmLeaseV1 RealmLease { get; }
}

internal abstract record GlobalParticipantAllocatorDurableSnapshotResultV1
{
    private GlobalParticipantAllocatorDurableSnapshotResultV1() { }
    internal sealed record Current(GlobalParticipantAllocatorExactRecordSnapshotV1 Snapshot) : GlobalParticipantAllocatorDurableSnapshotResultV1;
    internal sealed record RealmFenced(ulong CurrentFenceEpoch) : GlobalParticipantAllocatorDurableSnapshotResultV1;
    internal sealed record StoreUnavailable(BoundedAscii SafeCode) : GlobalParticipantAllocatorDurableSnapshotResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode) : GlobalParticipantAllocatorDurableSnapshotResultV1;
}

internal interface IGlobalParticipantAllocatorDurableSnapshotPortV1
{ ValueTask<GlobalParticipantAllocatorDurableSnapshotResultV1> ReadAsync(GlobalParticipantAllocatorDurableSnapshotRequestV1 request, CancellationToken cancellationToken); }
