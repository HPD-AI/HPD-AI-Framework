using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class FileGlobalParticipantAllocatorRealmStoreTests
{
    [Fact]
    public async Task BindRequiresAbsoluteRootAndPlatformBoundaryIsClosed()
    {
        await Assert.ThrowsAsync<ArgumentException>(async()=>await FileGlobalParticipantAllocatorRealmStore.BindAsync("relative",default));
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var store=await FileGlobalParticipantAllocatorRealmStore.BindAsync(root,default);
            var request=new GlobalParticipantAllocatorRealmCreateRequestV1(GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)1,16).ToArray())),new(Hash256.FromBytes(Enumerable.Repeat((byte)2,32).ToArray())));
            var result=await ((IGlobalParticipantAllocatorRealmPortV1)store).CreateAsync(request,default);
            if(!OperatingSystem.IsLinux())Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Unsupported>(result);
            else Assert.True(result is GlobalParticipantAllocatorRealmCreateResultV1.Created or GlobalParticipantAllocatorRealmCreateResultV1.StoreUnavailable or GlobalParticipantAllocatorRealmCreateResultV1.Unsupported or GlobalParticipantAllocatorRealmCreateResultV1.RootConflict or GlobalParticipantAllocatorRealmCreateResultV1.Incompatible);
        }
        finally
        {
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }

    [Fact]
    public void InventoryAndLinuxLayoutsAreFrozen()
    {
        Assert.Equal(144,System.Runtime.InteropServices.Marshal.SizeOf<FileGlobalParticipantAllocatorRealmStore.LinuxStatX64>());
        Assert.Equal(128,System.Runtime.InteropServices.Marshal.SizeOf<FileGlobalParticipantAllocatorRealmStore.LinuxStatArm64>());
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(FileGlobalParticipantAllocatorRealmStore.RootCustody)));
        var native=typeof(FileGlobalParticipantAllocatorRealmStore.LinuxNative);
        Assert.Equal(2,(int)native.GetField("O_RDWR",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetRawConstantValue()!);
        var custody=typeof(FileGlobalParticipantAllocatorRealmStore.RootCustody);
        Assert.NotNull(custody.GetField("_rootIdentityValid",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic));
    }

    [Fact]
    public async Task TestingTransportCreatesCanonicalRootAndReopensWithHigherFence()
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-managed-"+Guid.NewGuid().ToString("N"));var stages=new List<string>();
        var journal=GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)4,16).ToArray()));var request=new GlobalParticipantAllocatorRealmCreateRequestV1(journal,new(Hash256.FromBytes(Enumerable.Repeat((byte)5,32).ToArray())));
        await using(var store=await FileGlobalParticipantAllocatorRealmStore.BindForTestingAsync(root,x=>{stages.Add(x.ToString());return ValueTask.CompletedTask;},default))
        {var created=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)store).CreateAsync(request,default));Assert.Equal(1UL,created.RealmLease.Manifest.FenceEpoch);Assert.Equal(400,FileGlobalParticipantAllocatorDurableBackend.ManagedTestingTransport.GetForTesting(root).ReadDurableOwned("committed.head").Length);await created.RealmLease.DisposeAsync();}
        await using(var store=await FileGlobalParticipantAllocatorRealmStore.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default))
        {var opened=Assert.IsType<GlobalParticipantAllocatorRealmOpenResultV1.Opened>(await ((IGlobalParticipantAllocatorRealmPortV1)store).OpenAsync(new(journal,request.StoreBinding),default));Assert.Equal(2UL,opened.RealmLease.Manifest.FenceEpoch);await opened.RealmLease.DisposeAsync();}
        Assert.Contains("before-custody",stages);Assert.False(Directory.Exists(root));FileGlobalParticipantAllocatorDurableBackend.ManagedTestingTransport.ResetForTesting(root);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(54)]
    [InlineData(95)]
    public async Task ManifestDomainVersionEpochAndHashMutationsAreIncompatible(int offset)
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-manifest-"+Guid.NewGuid().ToString("N"));var journal=GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)14,16).ToArray()));var binding=new GlobalParticipantAllocatorStoreBindingV1(Hash256.FromBytes(Enumerable.Repeat((byte)15,32).ToArray()));await using(var store=await FileGlobalParticipantAllocatorRealmStore.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default)){var created=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(await ((IGlobalParticipantAllocatorRealmPortV1)store).CreateAsync(new(journal,binding),default));await created.RealmLease.DisposeAsync();}var volume=FileGlobalParticipantAllocatorDurableBackend.ManagedTestingTransport.GetForTesting(root);volume.CorruptDurableByte("realm.manifest",offset,1);volume.Crash();await using(var store=await FileGlobalParticipantAllocatorRealmStore.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default))Assert.IsType<GlobalParticipantAllocatorRealmOpenResultV1.Incompatible>(await ((IGlobalParticipantAllocatorRealmPortV1)store).OpenAsync(new(journal,binding),default));FileGlobalParticipantAllocatorDurableBackend.ManagedTestingTransport.ResetForTesting(root);
    }

    [Theory]
    [InlineData("before-root-inspection",false)]
    [InlineData("before-custody",false)]
    [InlineData("after-store-id-invocation",true)]
    [InlineData("after-empty-log-invocation",true)]
    [InlineData("after-anchor-create-invocation",true)]
    [InlineData("after-manifest-invocation",true)]
    public async Task EveryRealmCreateFaultStageHasFrozenDisposition(string stage,bool invoked)
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-create-fault-"+Guid.NewGuid().ToString("N"));var journal=GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)24,16).ToArray()));var binding=new GlobalParticipantAllocatorStoreBindingV1(Hash256.FromBytes(Enumerable.Repeat((byte)25,32).ToArray()));await using(var store=await FileGlobalParticipantAllocatorRealmStore.BindForTestingAsync(root,s=>s.ToString()==stage?ValueTask.FromException(new IOException(stage)):ValueTask.CompletedTask,default)){var result=await ((IGlobalParticipantAllocatorRealmPortV1)store).CreateAsync(new(journal,binding),default);if(invoked)Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.OutcomeUnknown>(result);else Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.StoreUnavailable>(result);}FileGlobalParticipantAllocatorDurableBackend.ManagedTestingTransport.ResetForTesting(root);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ManagedPartialRootStatesResumeOrCloseExactly(int mode)
    {
        var root=Path.Combine(Path.GetTempPath(),"hpd-d27e-partial-"+Guid.NewGuid().ToString("N"));var journal=GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)34,16).ToArray()));var binding=new GlobalParticipantAllocatorStoreBindingV1(Hash256.FromBytes(Enumerable.Repeat((byte)35,32).ToArray()));await using(var store=await FileGlobalParticipantAllocatorRealmStore.BindForTestingAsync(root,_=>ValueTask.CompletedTask,default)){var volume=FileGlobalParticipantAllocatorDurableBackend.ManagedTestingTransport.GetForTesting(root);var storeBytes=new byte[32];binding.StoreIdentity.TryWriteBytes(storeBytes);if(mode!=2){volume.WriteVolatile("store.id",storeBytes);volume.FileFlush("store.id");}if(mode==1){volume.WriteVolatile("authority.log",new byte[]{1});volume.FileFlush("authority.log");}if(mode==2){var slot=FileGlobalParticipantAllocatorDurableBackend.EmptyAnchor(journal);volume.WriteVolatile("committed.head",slot.Concat(slot).ToArray());volume.FileFlush("committed.head");}volume.DirectoryFlush();var result=await ((IGlobalParticipantAllocatorRealmPortV1)store).CreateAsync(new(journal,binding),default);if(mode==0){var created=Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Created>(result);await created.RealmLease.DisposeAsync();}else if(mode==1)Assert.Equal("durable-state-invalid",Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.Incompatible>(result).SafeCode.ToString());else Assert.IsType<GlobalParticipantAllocatorRealmCreateResultV1.RootConflict>(result);}FileGlobalParticipantAllocatorDurableBackend.ManagedTestingTransport.ResetForTesting(root);
    }
}
