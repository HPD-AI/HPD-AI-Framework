using HPD.Agent.Authority;
namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class GlobalParticipantAllocatorDurablePortV1Tests
{
    [Fact]
    public async Task RequestOwnsBytesAndLeaseUseFencesAfterDisposal()
    {
        var lease = Lease(); var bytes = new byte[] { 1, 2, 3 };
        var request = new GlobalParticipantAllocatorDurableClaimRequestV1(lease, null, bytes, Fact());
        bytes[0] = 9;
        Assert.Equal(1, request.ExactCanonicalRecordBytes.Span[0]);
        await lease.DisposeAsync();
        Assert.False(lease.TryAcquireUse(out _));
    }

    [Fact]
    public void RequestsRejectInvalidRequiredValues()
    {
        var lease = Lease();
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorDurableClaimRequestV1(null!, null, default, Fact()));
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorDurableClaimRequestV1(lease, null, default, default));
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorReconcileRequestV1(null!, Fact()));
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorReconcileRequestV1(lease, default));
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorDurableSnapshotRequestV1(null!));
    }

    [Fact]
    public void ResultUnionsExposeEveryClosedArmAndExactFieldType()
    {
        Assert.Equal(10, new[] { typeof(GlobalParticipantAllocatorDurableClaimResultV1.Committed), typeof(GlobalParticipantAllocatorDurableClaimResultV1.AlreadyCommitted), typeof(GlobalParticipantAllocatorDurableClaimResultV1.ContradictoryDuplicate), typeof(GlobalParticipantAllocatorDurableClaimResultV1.HeadConflict), typeof(GlobalParticipantAllocatorDurableClaimResultV1.InvalidRecord), typeof(GlobalParticipantAllocatorDurableClaimResultV1.LifetimeExhausted), typeof(GlobalParticipantAllocatorDurableClaimResultV1.RealmFenced), typeof(GlobalParticipantAllocatorDurableClaimResultV1.StoreUnavailable), typeof(GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown), typeof(GlobalParticipantAllocatorDurableClaimResultV1.Quarantined) }.Distinct().Count());
        Assert.Equal(6, new[] { typeof(GlobalParticipantAllocatorReconcileResultV1.Committed), typeof(GlobalParticipantAllocatorReconcileResultV1.NotFound), typeof(GlobalParticipantAllocatorReconcileResultV1.RealmFenced), typeof(GlobalParticipantAllocatorReconcileResultV1.StoreUnavailable), typeof(GlobalParticipantAllocatorReconcileResultV1.OutcomeUnknown), typeof(GlobalParticipantAllocatorReconcileResultV1.Quarantined) }.Distinct().Count());
        Assert.Equal(4, new[] { typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.Current), typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.RealmFenced), typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.StoreUnavailable), typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.Quarantined) }.Distinct().Count());
        Assert.Equal(typeof(ReadOnlyMemory<byte>), typeof(GlobalParticipantAllocatorDurableClaimResultV1.Committed).GetProperty("ExactCanonicalRecordBytes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.PropertyType);
        Assert.Equal(typeof(GlobalParticipantAllocatorExactRecordSnapshotV1), typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.Current).GetProperty("Snapshot")!.PropertyType);
        Properties(typeof(GlobalParticipantAllocatorDurableClaimResultV1.ContradictoryDuplicate), ("FactId", typeof(JournalFactId)), ("SafeCode", typeof(BoundedAscii)));
        Properties(typeof(GlobalParticipantAllocatorDurableClaimResultV1.HeadConflict), ("CurrentHead", typeof(GlobalParticipantAuthorityHeadV1?)));
        Properties(typeof(GlobalParticipantAllocatorDurableClaimResultV1.LifetimeExhausted), ("RecordCount", typeof(ulong)), ("TotalCanonicalRecordBytes", typeof(ulong)));
        Properties(typeof(GlobalParticipantAllocatorDurableClaimResultV1.RealmFenced), ("CurrentFenceEpoch", typeof(ulong)));
        Properties(typeof(GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown), ("FactId", typeof(JournalFactId)), ("SafeCode", typeof(BoundedAscii)));
        Properties(typeof(GlobalParticipantAllocatorReconcileResultV1.Committed), ("Head", typeof(GlobalParticipantAuthorityHeadV1)), ("Sequence", typeof(ulong)), ("ExactCanonicalRecordBytes", typeof(ReadOnlyMemory<byte>)));
        Properties(typeof(GlobalParticipantAllocatorDurableSnapshotResultV1.Current), ("Snapshot", typeof(GlobalParticipantAllocatorExactRecordSnapshotV1)));
    }

    [Fact]
    public void CommittedResultsOwnFreshBytes()
    {
        var source = new byte[] { 1, 2 }; var head = new GlobalParticipantAuthorityHeadV1(new(Journal(), 1), Hash(9));
        var committed = new GlobalParticipantAllocatorDurableClaimResultV1.Committed(head, 1, source);
        var retry = new GlobalParticipantAllocatorDurableClaimResultV1.AlreadyCommitted(head, 1, source);
        var reconciled = new GlobalParticipantAllocatorReconcileResultV1.Committed(head, 1, source);
        source[0] = 7;
        Assert.Equal(1, committed.ExactCanonicalRecordBytes.Span[0]); Assert.Equal(1, retry.ExactCanonicalRecordBytes.Span[0]); Assert.Equal(1, reconciled.ExactCanonicalRecordBytes.Span[0]);
    }

    private static void Properties([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)] Type arm, params (string Name, Type Type)[] expected)
    {
        var actual = arm.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly).Where(x => x.Name != "EqualityContract").Select(x => (x.Name, x.PropertyType)).OrderBy(x => x.Name).ToArray();
        Assert.Equal(expected.OrderBy(x => x.Name).ToArray(), actual);
    }

    private static StableId128 Id(byte value) => StableId128.FromBytes(Enumerable.Repeat(value, 16).ToArray());
    private static GlobalParticipantAllocatorJournalId Journal() => GlobalParticipantAllocatorJournalId.FromValue(Id(1));
    private static JournalFactId Fact() => JournalFactId.FromValue(Id(2));
    private static GlobalParticipantAllocatorRealmLeaseV1 Lease() { var store=Hash(3);var time=new UtcInstant(0);return new(new(Journal(),1,1,store,time,GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(Journal(),1,1,store,time)),new TestCustody()); }
    private sealed class TestCustody : IGlobalParticipantAllocatorDurableCustodyV1 { public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
    private static Hash256 Hash(byte value) => Hash256.FromBytes(Enumerable.Repeat(value, 32).ToArray());
}
