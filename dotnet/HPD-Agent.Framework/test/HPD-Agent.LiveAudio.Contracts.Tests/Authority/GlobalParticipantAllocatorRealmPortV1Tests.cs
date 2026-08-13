using System.Security.Cryptography;
using HPD.Agent.Authority;
namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class GlobalParticipantAllocatorRealmPortV1Tests
{
    [Fact]
    public async Task LeaseWaitsForUseAndReleasesCustodyExactlyOnce()
    {
        var custody = new MemoryStream(); var lease = Lease(custody);
        Assert.True(lease.TryAcquireUse(out var use));
        var first = lease.DisposeAsync().AsTask(); var second = lease.DisposeAsync().AsTask();
        Assert.Same(first, second);
        Assert.False(first.IsCompleted); Assert.False(lease.TryAcquireUse(out _));
        await use.DisposeAsync(); await Task.WhenAll(first, second);
        Assert.Throws<ObjectDisposedException>(() => custody.ReadByte());
        use.Dispose();
    }

    [Fact]
    public async Task AcquireDisposeRaceClosesWithoutLeakingUses()
    {
        var lease=Lease(new MemoryStream());
        var attempts=Enumerable.Range(0,128).Select(_=>Task.Run(async()=>{if(lease.TryAcquireUse(out var use)){await Task.Yield();await use.DisposeAsync();}})).ToArray();
        var disposal=lease.DisposeAsync().AsTask();
        await Task.WhenAll(attempts);await disposal;
        Assert.False(lease.TryAcquireUse(out _));
        Assert.True(lease.DisposeAsync().IsCompletedSuccessfully);
    }

    [Fact]
    public async Task CustodyFailureIsCachedAndSharedWithoutRetry()
    {
        var custody=Lease(new MemoryStream());
        typeof(GlobalParticipantAllocatorRealmLeaseV1).GetField("_disposeTask",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(custody,Task.FromException(new InvalidOperationException("custody")));
        var lease=Lease(custody);var first=lease.DisposeAsync().AsTask();var second=lease.DisposeAsync().AsTask();
        Assert.Same(first,second);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>first);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>lease.DisposeAsync().AsTask());
    }

    [Fact]
    public void RealmRequestsAndBindingRejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorStoreBindingV1(default));
        var binding = new GlobalParticipantAllocatorStoreBindingV1(Hash(2));
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorRealmCreateRequestV1(default, binding));
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorRealmCreateRequestV1(Journal(), null!));
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorRealmOpenRequestV1(default, binding));
        Assert.Throws<ArgumentException>(() => new GlobalParticipantAllocatorRealmOpenRequestV1(Journal(), null!));
    }

    [Fact]
    public void ManifestRejectsEveryInvalidOrUnauthenticatedField()
    {
        var journal=Journal();var store=Hash(2);var created=new UtcInstant(7);var valid=GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(journal,1,9,store,created);
        Assert.NotNull(new GlobalParticipantAllocatorRealmManifestV1(journal,1,9,store,created,valid));
        Assert.Throws<ArgumentException>(()=>new GlobalParticipantAllocatorRealmManifestV1(default,1,9,store,created,valid));
        Assert.Throws<ArgumentException>(()=>new GlobalParticipantAllocatorRealmManifestV1(journal,2,9,store,created,valid));
        Assert.Throws<ArgumentException>(()=>new GlobalParticipantAllocatorRealmManifestV1(journal,1,0,store,created,valid));
        Assert.Throws<ArgumentException>(()=>new GlobalParticipantAllocatorRealmManifestV1(journal,1,ulong.MaxValue,store,created,valid));
        Assert.Throws<ArgumentException>(()=>new GlobalParticipantAllocatorRealmManifestV1(journal,1,9,default,created,valid));
        Assert.Throws<ArgumentException>(()=>new GlobalParticipantAllocatorRealmManifestV1(journal,1,9,store,new UtcInstant(8),valid));
        Assert.Throws<ArgumentException>(()=>new GlobalParticipantAllocatorRealmManifestV1(journal,1,9,store,created,Hash(8)));
    }

    [Fact]
    public void ResultUnionsExposeEveryClosedArm()
    {
        Assert.Equal(7, new[] { typeof(GlobalParticipantAllocatorRealmCreateResultV1.Created), typeof(GlobalParticipantAllocatorRealmCreateResultV1.AlreadyExists), typeof(GlobalParticipantAllocatorRealmCreateResultV1.Incompatible), typeof(GlobalParticipantAllocatorRealmCreateResultV1.RootConflict), typeof(GlobalParticipantAllocatorRealmCreateResultV1.Unsupported), typeof(GlobalParticipantAllocatorRealmCreateResultV1.StoreUnavailable), typeof(GlobalParticipantAllocatorRealmCreateResultV1.OutcomeUnknown) }.Distinct().Count());
        Assert.Equal(8, new[] { typeof(GlobalParticipantAllocatorRealmOpenResultV1.Opened), typeof(GlobalParticipantAllocatorRealmOpenResultV1.NotFound), typeof(GlobalParticipantAllocatorRealmOpenResultV1.Incompatible), typeof(GlobalParticipantAllocatorRealmOpenResultV1.RootConflict), typeof(GlobalParticipantAllocatorRealmOpenResultV1.Unsupported), typeof(GlobalParticipantAllocatorRealmOpenResultV1.StoreUnavailable), typeof(GlobalParticipantAllocatorRealmOpenResultV1.OutcomeUnknown), typeof(GlobalParticipantAllocatorRealmOpenResultV1.Quarantined) }.Distinct().Count());
        Properties(typeof(GlobalParticipantAllocatorRealmCreateResultV1.Created), ("RealmLease", typeof(GlobalParticipantAllocatorRealmLeaseV1)));
        Properties(typeof(GlobalParticipantAllocatorRealmCreateResultV1.AlreadyExists), ("Manifest", typeof(GlobalParticipantAllocatorRealmManifestV1)));
        Properties(typeof(GlobalParticipantAllocatorRealmCreateResultV1.OutcomeUnknown), ("JournalId", typeof(GlobalParticipantAllocatorJournalId)), ("SafeCode", typeof(BoundedAscii)));
        Properties(typeof(GlobalParticipantAllocatorRealmOpenResultV1.Opened), ("RealmLease", typeof(GlobalParticipantAllocatorRealmLeaseV1)));
        Properties(typeof(GlobalParticipantAllocatorRealmOpenResultV1.Quarantined), ("SafeCode", typeof(BoundedAscii)));
    }

    [Theory]
    [InlineData("01010101010101010101010101010101", "0202020202020202020202020202020202020202020202020202020202020202", 1UL, 0L, "5eb3a2d83c4dc1c14b75aab7bbdcf8f6fa028f214458ec2672543e59a8812519")]
    [InlineData("02020202020202020202020202020202", "0101010101010101010101010101010101010101010101010101010101010101", 18446744073709551614UL, 9223372036854775807L, "25bd7f754597d147ada28f0b0f3a0504eefa5d69ea0b42304d59cd7d00eafc77")]
    public void CanonicalManifestGolden(string journal, string store, ulong epoch, long created, string expected)
    {
        var bytes = new byte[95]; var domain = "hpd-s1-gpa-realm-manifest-v1\0"u8;
        domain.CopyTo(bytes); Convert.FromHexString(journal).CopyTo(bytes, 29);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(45), 1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(47), epoch);
        Convert.FromHexString(store).CopyTo(bytes, 55);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(87), created);
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var journalId=GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Convert.FromHexString(journal)));var storeHash=Hash256.FromBytes(Convert.FromHexString(store));var createdAt=new UtcInstant(created);
        var computed=GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(journalId,1,epoch,storeHash,createdAt);
        Assert.Equal(expected,computed.ToString());Assert.Equal(computed,new GlobalParticipantAllocatorRealmManifestV1(journalId,1,epoch,storeHash,createdAt,computed).ManifestHash);
    }

    private static void Properties([System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)] Type arm, params (string Name, Type Type)[] expected)
    {
        var actual=arm.GetProperties(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly).Where(x=>x.Name!="EqualityContract").Select(x=>(x.Name,x.PropertyType)).OrderBy(x=>x.Name).ToArray();
        Assert.Equal(expected.OrderBy(x=>x.Name).ToArray(),actual);
    }

    private static StableId128 Id(byte value) => StableId128.FromBytes(Enumerable.Repeat(value, 16).ToArray());
    private static GlobalParticipantAllocatorJournalId Journal() => GlobalParticipantAllocatorJournalId.FromValue(Id(1));
    private static Hash256 Hash(byte value) => Hash256.FromBytes(Enumerable.Repeat(value, 32).ToArray());
    private static GlobalParticipantAllocatorRealmLeaseV1 Lease(IAsyncDisposable custody) { var store=Hash(3);var time=new UtcInstant(0);return new(new(Journal(),1,1,store,time,GlobalParticipantAllocatorRealmManifestV1.ComputeManifestHash(Journal(),1,1,store,time)),custody); }
}
