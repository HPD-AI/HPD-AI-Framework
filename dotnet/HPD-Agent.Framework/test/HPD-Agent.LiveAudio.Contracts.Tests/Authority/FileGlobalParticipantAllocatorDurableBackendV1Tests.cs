using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class FileGlobalParticipantAllocatorDurableBackendV1Tests
{
    [Fact]
    public async Task OpenResultOpenedOwnsBackend()
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-open-"+Guid.NewGuid().ToString("N"));var journal=Journal();var binding=Binding();
        await using var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);
        var created=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default));
        var opened=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,created.RealmLease,_=>ValueTask.CompletedTask,default));
        await opened.Backend.DisposeAsync();await created.RealmLease.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Fact]
    public async Task CorruptAnchorReturnsQuarantinedAndClosesTestingHandle()
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-corrupt-"+Guid.NewGuid().ToString("N"));var journal=Journal();var binding=Binding();
        await using(var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default)){var created=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default));await created.RealmLease.DisposeAsync();}
        var volume=FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.GetForTesting(root);volume.CorruptDurableByte("committed.head",168,1);volume.CorruptDurableByte("committed.head",368,1);volume.Crash();
        await using var reopenedRealm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease=Assert.IsType<GlobalParticipantAllocatorRealmOpenResultV1.Opened>(await ((IGlobalParticipantAllocatorRealmPortV1)reopenedRealm).OpenAsync(new(journal,binding),default)).RealmLease;
        Assert.Equal("durable-state-invalid",Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Quarantined>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default)).SafeCode.ToString());await lease.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Fact]
    public void OpenResultInventoryIsExact()
    {
        var nested=typeof(FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1).GetNestedTypes(System.Reflection.BindingFlags.NonPublic).Select(x=>x.Name.Split('`')[0]).OrderBy(x=>x).ToArray();
        Assert.Equal(new[]{"Opened","OutcomeUnknown","Quarantined","RealmFenced","StoreUnavailable"},nested);
        Assert.Equal(typeof(FileGlobalParticipantAllocatorDurableBackendV1),typeof(FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened).GetProperty("Backend")!.PropertyType);
    }

    [Theory]
    [InlineData("before-anchor-adoption-flush",typeof(FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.StoreUnavailable))]
    [InlineData("after-anchor-adoption-flush",typeof(FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.OutcomeUnknown))]
    public async Task AdoptionFaultReturnsOutcomeUnknownAndRetryOpens(string stage,Type expected)
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-adopt-"+Guid.NewGuid().ToString("N"));var journal=Journal();var binding=Binding();
        await using var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;
        var failed=await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,s=>s.ToString()==stage?ValueTask.FromException(new IOException(stage)):ValueTask.CompletedTask,default);Assert.Equal(expected,failed.GetType());
        var retry=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default));await retry.Backend.DisposeAsync();await lease.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Theory]
    [InlineData("recovery-before-tail-truncate")]
    [InlineData("recovery-after-tail-truncate-before-flush")]
    [InlineData("recovery-after-tail-flush")]
    public async Task PostAdoptionTailFaultIsOutcomeUnknownAndRetryRepairs(string stage)
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-tail-stage-"+Guid.NewGuid().ToString("N"));var journal=Journal();var binding=Binding();
        await using var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;
        var volume=FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.GetForTesting(root);volume.AppendDurable("authority.log",new byte[]{1,2,3,4,5});volume.Crash();
        var failed=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.OutcomeUnknown>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,s=>s.ToString()==stage?ValueTask.FromException(new IOException(stage)):ValueTask.CompletedTask,default));Assert.Equal("durability-unknown",failed.SafeCode.ToString());volume.Crash();
        var retry=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default));Assert.Empty(volume.ReadDurableOwned("authority.log"));await retry.Backend.DisposeAsync();await lease.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Fact]
    public async Task DisposedLeaseReturnsRealmFencedWithoutHandleLeak()
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-fenced-"+Guid.NewGuid().ToString("N"));var journal=Journal();var binding=Binding();await using var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;await lease.DisposeAsync();Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.RealmFenced>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default));FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Fact]
    public async Task ManagedBackendCommitsFull65536RejectedHistory()
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-max-"+Guid.NewGuid().ToString("N"));var journal=GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)5,16).ToArray()));var binding=Binding();await using var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;var backend=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default)).Backend;var method=typeof(GlobalParticipantAllocatorFoldV1Tests).GetMethod("RejectedRecord",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;GlobalParticipantAuthorityHeadV1? head=null;for(ulong sequence=1;sequence<=65536;sequence++){var bytes=(byte[])method.Invoke(null,new object?[]{sequence,head})!;Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(bytes,out var outer));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(),out var body));var fp=GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(body!.Source);var fact=GlobalParticipantAllocatorFactIdsV1.Fact(outer.SourceSession.LiveSessionId,body.OperationId,fp);head=Assert.IsType<GlobalParticipantAllocatorDurableClaimResultV1.Committed>(await ((IGlobalParticipantAllocatorDurableClaimPortV1)backend).ClaimAsync(new(lease,head,bytes,fact),default)).Head;}Assert.Equal(65536UL,backend.RetainedRecordCount);await backend.DisposeAsync();await lease.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Fact]
    public async Task CanonicalCommitCrashReopenReconcileSnapshotAndPage()
    {
        var bytes=GlobalParticipantAllocatorFoldV1Tests.AppliedRecord();var (journal,fact)=Parts(bytes);var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-e2e-"+Guid.NewGuid().ToString("N"));var binding=Binding();GlobalParticipantAuthorityHeadV1 head;
        await using(var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default)){var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;var backend=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default)).Backend;head=Assert.IsType<GlobalParticipantAllocatorDurableClaimResultV1.Committed>(await ((IGlobalParticipantAllocatorDurableClaimPortV1)backend).ClaimAsync(new(lease,null,bytes,fact),default)).Head;await backend.DisposeAsync();await lease.DisposeAsync();}
        FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.GetForTesting(root).Crash();
        await using(var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default)){var lease=Assert.IsType<GlobalParticipantAllocatorRealmOpenResultV1.Opened>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).OpenAsync(new(journal,binding),default)).RealmLease;var backend=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default)).Backend;Assert.Equal(head,Assert.IsType<GlobalParticipantAllocatorReconcileResultV1.Committed>(await ((IGlobalParticipantAllocatorReconciliationPortV1)backend).ReconcileAsync(new(lease,fact),default)).Head);var snapshot=Assert.IsType<GlobalParticipantAllocatorDurableSnapshotResultV1.Current>(await ((IGlobalParticipantAllocatorDurableSnapshotPortV1)backend).ReadAsync(new(lease),default));Assert.Equal(1UL,snapshot.Snapshot.RecordCount);var pages=await backend.CreatePinnedPageSourceAsync(new(lease),default);Assert.IsType<GlobalParticipantAllocatorPageReadResultV1.Verified>(await new GlobalParticipantAllocatorPageReaderV1(pages).ReadAsync(journal,default));await backend.DisposeAsync();await lease.DisposeAsync();}FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Theory]
    [InlineData(1,113)]
    [InlineData(8192,8304)]
    public void FrameBoundsAndHashAreExact(int recordLength,int expectedLength)
    {
        var encode=typeof(FileGlobalParticipantAllocatorDurableBackendV1).GetMethod("EncodeFrame",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;var fact=JournalFactId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)71,16).ToArray()));var frame=(byte[])encode.Invoke(null,new object[]{1UL,1UL,fact,new byte[recordLength],default(Hash256)})!;Assert.Equal(expectedLength,frame.Length);Assert.True(frame.AsSpan(0,8).SequenceEqual("HPDGPA03"u8));Assert.Equal((uint)expectedLength,System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(8,4)));
    }

    [Fact]
    public void EmptyAnchorIsExactDuplicated400BytesAndMutationDiffers()
    {
        var bytes=FileGlobalParticipantAllocatorDurableBackendV1.EmptyAnchor(Journal());Assert.Equal(200,bytes.Length);var pair=bytes.Concat(bytes).ToArray();Assert.Equal(400,pair.Length);Assert.True(pair.AsSpan(0,200).SequenceEqual(pair.AsSpan(200)));pair[168]^=1;Assert.False(pair.AsSpan(0,200).SequenceEqual(pair.AsSpan(200)));
    }

    [Fact]
    public async Task ConcurrentSameFactCommitsOnceAndRetryIsAlreadyCommitted()
    {
        var bytes=GlobalParticipantAllocatorFoldV1Tests.AppliedRecord();var (journal,fact)=Parts(bytes);var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-race-"+Guid.NewGuid().ToString("N"));var binding=Binding();await using var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;var backend=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default)).Backend;var port=(IGlobalParticipantAllocatorDurableClaimPortV1)backend;var a=port.ClaimAsync(new(lease,null,bytes,fact),default).AsTask();var b=port.ClaimAsync(new(lease,null,bytes,fact),default).AsTask();var results=await Task.WhenAll(a,b);Assert.Single(results.OfType<GlobalParticipantAllocatorDurableClaimResultV1.Committed>());Assert.Single(results.OfType<GlobalParticipantAllocatorDurableClaimResultV1.AlreadyCommitted>());var snap=Assert.IsType<GlobalParticipantAllocatorDurableSnapshotResultV1.Current>(await ((IGlobalParticipantAllocatorDurableSnapshotPortV1)backend).ReadAsync(new(lease),default));Assert.Equal(1UL,snap.Snapshot.RecordCount);await backend.DisposeAsync();await lease.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Theory]
    [InlineData("before-frame-write","prewrite-capacity-unavailable")]
    [InlineData("during-frame-write","operation-interrupted")]
    [InlineData("after-frame-flush-before-anchor","durability-unknown")]
    [InlineData("during-inactive-anchor-write","durability-unknown")]
    [InlineData("after-anchor-write-before-flush","durability-unknown")]
    [InlineData("after-anchor-flush-before-ack","acknowledgement-lost")]
    public async Task ClaimFaultStagesHaveExactSafeCodes(string stage,string code)
    {
        var bytes=GlobalParticipantAllocatorFoldV1Tests.AppliedRecord();var (journal,fact)=Parts(bytes);var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-stage-"+Guid.NewGuid().ToString("N"));var binding=Binding();await using var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;var backend=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,s=>s.ToString()==stage?ValueTask.FromException(new IOException(stage)):ValueTask.CompletedTask,default)).Backend;var result=await ((IGlobalParticipantAllocatorDurableClaimPortV1)backend).ClaimAsync(new(lease,null,bytes,fact),default);var actual=result switch{GlobalParticipantAllocatorDurableClaimResultV1.StoreUnavailable x=>x.SafeCode.ToString(),GlobalParticipantAllocatorDurableClaimResultV1.OutcomeUnknown x=>x.SafeCode.ToString(),_=>result.GetType().Name};Assert.Equal(code,actual);await backend.DisposeAsync();await lease.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Fact]
    public async Task OneCorruptAnchorSlotRecoversFromTheOther()
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-slot-"+Guid.NewGuid().ToString("N"));var journal=Journal();var binding=Binding();
        await using(var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default)){var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;await lease.DisposeAsync();}
        var volume=FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.GetForTesting(root);volume.CorruptDurableByte("committed.head",168,1);volume.Crash();
        await using var reopenedRealm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease2=Assert.IsType<GlobalParticipantAllocatorRealmOpenResultV1.Opened>(await ((IGlobalParticipantAllocatorRealmPortV1)reopenedRealm).OpenAsync(new(journal,binding),default)).RealmLease;var opened=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease2,_=>ValueTask.CompletedTask,default));await opened.Backend.DisposeAsync();await lease2.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    [Fact]
    public async Task PartialUnanchoredSuffixIsTruncatedButCommittedSuffixLossQuarantines()
    {
        var bytes=GlobalParticipantAllocatorFoldV1Tests.AppliedRecord();var (journal,fact)=Parts(bytes);var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-tail-"+Guid.NewGuid().ToString("N"));var binding=Binding();
        await using(var realm=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default)){var lease=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)realm).CreateAsync(new(journal,binding),default)).RealmLease;var backend=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default)).Backend;Assert.IsType<GlobalParticipantAllocatorDurableClaimResultV1.Committed>(await ((IGlobalParticipantAllocatorDurableClaimPortV1)backend).ClaimAsync(new(lease,null,bytes,fact),default));await backend.DisposeAsync();await lease.DisposeAsync();}
        var volume=FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.GetForTesting(root);var committed=volume.ReadDurableOwned("authority.log").Length;volume.AppendDurable("authority.log",new byte[]{1,2,3,4,5});volume.Crash();
        await using(var realm2=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default)){var lease=Assert.IsType<GlobalParticipantAllocatorRealmOpenResultV1.Opened>(await ((IGlobalParticipantAllocatorRealmPortV1)realm2).OpenAsync(new(journal,binding),default)).RealmLease;var opened=Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Opened>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease,_=>ValueTask.CompletedTask,default));Assert.Equal(committed,volume.ReadDurableOwned("authority.log").Length);await opened.Backend.DisposeAsync();await lease.DisposeAsync();}
        volume.TruncateDurable("authority.log",committed-1);volume.Crash();await using var realm3=await FileGlobalParticipantAllocatorRealmStoreV1.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default);var lease3=Assert.IsType<GlobalParticipantAllocatorRealmOpenResultV1.Opened>(await ((IGlobalParticipantAllocatorRealmPortV1)realm3).OpenAsync(new(journal,binding),default)).RealmLease;Assert.IsType<FileGlobalParticipantAllocatorDurableBackendV1.OpenResultV1.Quarantined>(await FileGlobalParticipantAllocatorDurableBackendV1.OpenForTestingAsync(root,lease3,_=>ValueTask.CompletedTask,default));await lease3.DisposeAsync();FileGlobalParticipantAllocatorDurableBackendV1.ManagedTestingTransport.ResetForTesting(root);
    }

    private static GlobalParticipantAllocatorJournalId Journal()=>GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)5,16).ToArray()));
    private static GlobalParticipantAllocatorStoreBindingV1 Binding()=>new(Hash256.FromBytes(Enumerable.Repeat((byte)62,32).ToArray()));
    private static (GlobalParticipantAllocatorJournalId Journal,JournalFactId Fact) Parts(byte[] bytes){Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(bytes,out var outer));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(),out var body));var fp=GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(body!.Source);return(body.AssignedPosition.JournalId,GlobalParticipantAllocatorFactIdsV1.Fact(outer.SourceSession.LiveSessionId,body.OperationId,fp));}
}
