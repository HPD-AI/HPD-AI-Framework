using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;
using System.Collections.Immutable;
using System.Reflection;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;
public sealed class InMemoryGlobalParticipantAllocatorConformanceBackendV1Tests
{
    [Fact] public async Task InvalidBytesAreClosed(){var id=GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)1,16).ToArray()));IGlobalParticipantAllocatorClaimPortV1 b=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var r=await b.ClaimAsync(new(id,null,new byte[]{1},default),default);Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(r);}

    [Fact] public async Task CommitRetryAndSnapshotUseFreshOwnedBytes()
    {var (id,request)=Request();var backend=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var port=(IGlobalParticipantAllocatorClaimPortV1)backend;var committed=Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await port.ClaimAsync(request,default));var retry=Assert.IsType<GlobalParticipantAllocatorClaimResultV1.AlreadyCommitted>(await port.ClaimAsync(request,default));Assert.Equal(committed.Head,retry.Head);var snapshot=Assert.IsType<GlobalParticipantAllocatorSnapshotResultV1.Current>(((IGlobalParticipantAllocatorExactRecordSnapshotReaderV1)backend).ReadExactSnapshot()).Snapshot;Assert.Equal(1UL,snapshot.RecordCount);var copy=snapshot.ExactCanonicalRecords[0].ToArray();copy[0]^=0xff;Assert.Equal(request.ExactCanonicalRecordBytes.Span[0],snapshot.ExactCanonicalRecords[0].Span[0]);}

    [Fact] public async Task WrongExpectedHeadConflictsWithoutMutation()
    {var (id,request)=Request();var backend=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var port=(IGlobalParticipantAllocatorClaimPortV1)backend;var wrong=new GlobalParticipantAllocatorClaimRequestV1(id,new GlobalParticipantAuthorityHeadV1(new GlobalParticipantAuthorityPositionV1(id,1),Hash256.FromBytes(Enumerable.Repeat((byte)7,32).ToArray())),request.ExactCanonicalRecordBytes,request.FactId);Assert.IsType<GlobalParticipantAllocatorClaimResultV1.HeadConflict>(await port.ClaimAsync(wrong,default));Assert.Equal(0UL,Assert.IsType<GlobalParticipantAllocatorSnapshotResultV1.Current>(((IGlobalParticipantAllocatorExactRecordSnapshotReaderV1)backend).ReadExactSnapshot()).Snapshot.RecordCount);}

    [Fact] public async Task ChangedCanonicalBytesForSameFactTerminallyQuarantine()
    {var (id,request)=Request();var backend=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var port=(IGlobalParticipantAllocatorClaimPortV1)backend;Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await port.ClaimAsync(request,default));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(request.ExactCanonicalRecordBytes,out var outer));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(),out var body));var x=body!;var changedBody=new GlobalParticipantClaimRecordBodyV1(x.OperationId,x.Source,x.ParticipantId,x.PriorHead,x.OwnerProof,new ParticipantIdClaimOutcomeV1(3,null,new BoundedAscii("invalid-body")),x.AssignedPosition,x.ObservedAt);var changed=GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(outer.SourceSession,outer.SourceExpectedAuthority,GlobalParticipantAllocatorCodecsV1.Encode(changedBody)));Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Quarantined>(await port.ClaimAsync(new(id,null,changed,request.FactId),default));Assert.IsType<GlobalParticipantAllocatorSnapshotResultV1.Quarantined>(((IGlobalParticipantAllocatorExactRecordSnapshotReaderV1)backend).ReadExactSnapshot());Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Quarantined>(await port.ClaimAsync(request,default));}

    [Fact] public async Task CancellationAfterLockEntryCannotChangeClosedResult()
    {var (id,request)=Request();using var entered=new ManualResetEventSlim();using var proceed=new ManualResetEventSlim();using var cts=new CancellationTokenSource();var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id,entered,proceed);var task=Task.Run(async()=>await port.ClaimAsync(request,cts.Token));Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));cts.Cancel();proceed.Set();Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await task);}

    [Fact] public async Task CancellationBeforeEntryThrowsWithoutMutation()
    {var (id,request)=Request();var backend=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(async()=>await ((IGlobalParticipantAllocatorClaimPortV1)backend).ClaimAsync(request,cts.Token));Assert.Equal(0UL,Assert.IsType<GlobalParticipantAllocatorSnapshotResultV1.Current>(((IGlobalParticipantAllocatorExactRecordSnapshotReaderV1)backend).ReadExactSnapshot()).Snapshot.RecordCount);}

    [Fact] public async Task RetainedStateContradictionTerminallyQuarantinesBeforePublication()
    {
        var (id,request)=Request();var backend=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var port=(IGlobalParticipantAllocatorClaimPortV1)backend;
        Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await port.ClaimAsync(request,default));
        var field=typeof(InMemoryGlobalParticipantAllocatorConformanceBackendV1).GetField("_state",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var current=(InMemoryGlobalParticipantAllocatorConformanceBackendV1.State)field.GetValue(backend)!;
        field.SetValue(backend,current with{Facts=ImmutableDictionary<JournalFactId,(GlobalParticipantAuthorityHeadV1,ulong,ReadOnlyMemory<byte>)>.Empty});
        var result=Assert.IsType<GlobalParticipantAllocatorSnapshotResultV1.Quarantined>(((IGlobalParticipantAllocatorExactRecordSnapshotReaderV1)backend).ReadExactSnapshot());
        Assert.Equal("retained-state-invalid",result.SafeCode.ToString());
        Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Quarantined>(await port.ClaimAsync(request,default));
    }

    [Fact] public async Task InvalidCodePrecedenceClosesNullSizeJournalAndWire()
    {
        var (id,request)=Request();var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);
        static string Code(GlobalParticipantAllocatorClaimResultV1 value)=>Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(value).SafeCode.ToString();
        Assert.Equal("request-null",Code(await port.ClaimAsync(null!,default)));
        Assert.Equal("record-size-invalid",Code(await port.ClaimAsync(new(id,null,ReadOnlyMemory<byte>.Empty,default),default)));
        Assert.Equal("record-size-invalid",Code(await port.ClaimAsync(new(default,null,ReadOnlyMemory<byte>.Empty,default),default)));
        Assert.Equal("journal-invalid",Code(await port.ClaimAsync(new(default,null,request.ExactCanonicalRecordBytes,request.FactId),default)));
        var other=GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)19,16).ToArray()));
        Assert.Equal("record-size-invalid",Code(await port.ClaimAsync(new(other,null,ReadOnlyMemory<byte>.Empty,default),default)));
        Assert.Equal("journal-mismatch",Code(await port.ClaimAsync(new(other,null,request.ExactCanonicalRecordBytes,request.FactId),default)));
        Assert.Equal("record-wire-invalid",Code(await port.ClaimAsync(new(id,null,new byte[]{0x01},default),default)));
    }

    [Fact] public async Task ConcurrentSameClaimLinearizesAsCommitThenRetry()
    {
        var (id,request)=Request();var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);
        var results=await Task.WhenAll(Task.Run(async()=>await port.ClaimAsync(request,default)),Task.Run(async()=>await port.ClaimAsync(request,default)));
        Assert.Single(results.OfType<GlobalParticipantAllocatorClaimResultV1.Committed>());Assert.Single(results.OfType<GlobalParticipantAllocatorClaimResultV1.AlreadyCommitted>());
    }

    [Fact] public async Task DisposedSignalsAreContainedWithoutChangingCommit()
    {
        var (id,request)=Request();var entered=new ManualResetEventSlim();var proceed=new ManualResetEventSlim(true);entered.Dispose();proceed.Dispose();
        var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id,entered,proceed);
        Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await port.ClaimAsync(request,default));
    }

    [Fact] public async Task FactIdentityMismatchMapsToClosedInvalidCode()
    {
        var (id,request)=Request();var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);
        static string Code(GlobalParticipantAllocatorClaimResultV1 value)=>Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(value).SafeCode.ToString();
        Assert.Equal("fact-id-mismatch",Code(await port.ClaimAsync(new(id,null,request.ExactCanonicalRecordBytes,default),default)));
    }

    [Fact] public async Task MaximumRejectedHistoryUsesDirectClaimsAndSnapshotMatchesOneReplay()
    {
        var id=Journal();var backend=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var port=(IGlobalParticipantAllocatorClaimPortV1)backend;var replay=GlobalParticipantAllocatorFoldV1.Create(id);GlobalParticipantAuthorityHeadV1? head=null;
        for(ulong sequence=1;sequence<=65536;sequence++)
        {
            var bytes=RejectedRecord(sequence,head);var request=Request(bytes,head);var committed=Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await port.ClaimAsync(request,default));Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(replay.Apply(bytes));head=committed.Head;
        }
        var snapshot=Assert.IsType<GlobalParticipantAllocatorSnapshotResultV1.Current>(((IGlobalParticipantAllocatorExactRecordSnapshotReaderV1)backend).ReadExactSnapshot()).Snapshot;var expected=Assert.IsType<GlobalParticipantAllocatorFoldResultV1.Current>(replay.Complete()).Snapshot;
        Assert.Equal(65536UL,snapshot.RecordCount);Assert.Equal(expected.Head,snapshot.Head);Assert.Equal(expected.TotalCanonicalRecordBytes,snapshot.TotalCanonicalRecordBytes);
        var retry=Request(snapshot.ExactCanonicalRecords[^1].ToArray(),snapshot.ExactCanonicalRecords.Count==1?null:GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(snapshot.ExactCanonicalRecords[^1],out var retryOuter)&&GlobalParticipantAllocatorCodecsV1.TryDecodeBody(retryOuter!.BodyBytes.ToArray(),out var retryBody)?retryBody!.PriorHead:null);Assert.IsType<GlobalParticipantAllocatorClaimResultV1.AlreadyCommitted>(await port.ClaimAsync(retry,default));
        var unique=Request(RejectedRecord(1,null,70000),head);Assert.IsType<GlobalParticipantAllocatorClaimResultV1.LifetimeExhausted>(await port.ClaimAsync(unique,default));
        var stale=Request(RejectedRecord(1,null,70001),null);Assert.IsType<GlobalParticipantAllocatorClaimResultV1.HeadConflict>(await port.ClaimAsync(stale,default));
    }

    [Fact] public async Task RecordAndTotalByteBoundsAcceptExactMaximumAndRejectMaximumPlusOne()
    {
        var (id,request)=Request();var oversized=new byte[8193];var ordinary=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var ordinaryPort=(IGlobalParticipantAllocatorClaimPortV1)ordinary;
        Assert.Equal("record-size-invalid",Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(await ordinaryPort.ClaimAsync(new(id,null,oversized,default),default)).SafeCode.ToString());
        static void SetTotal(InMemoryGlobalParticipantAllocatorConformanceBackendV1 backend,ulong total)
        {var field=typeof(InMemoryGlobalParticipantAllocatorConformanceBackendV1).GetField("_state",BindingFlags.Instance|BindingFlags.NonPublic)!;var state=(InMemoryGlobalParticipantAllocatorConformanceBackendV1.State)field.GetValue(backend)!;field.SetValue(backend,state with{TotalCanonicalRecordBytes=total});}
        var exact=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);SetTotal(exact,536870912UL-(ulong)request.ExactCanonicalRecordBytes.Length);Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await ((IGlobalParticipantAllocatorClaimPortV1)exact).ClaimAsync(request,default));
        var over=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);SetTotal(over,536870913UL-(ulong)request.ExactCanonicalRecordBytes.Length);Assert.IsType<GlobalParticipantAllocatorClaimResultV1.LifetimeExhausted>(await ((IGlobalParticipantAllocatorClaimPortV1)over).ClaimAsync(request,default));
    }

    [Fact] public async Task DistinctValidFactsSharingExpectedHeadLinearizeCommitAndConflict()
    {
        var id=Journal();var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var a=Request(RejectedRecord(1,null,0),null);var b=Request(RejectedRecord(1,null,100),null);
        var results=await Task.WhenAll(Task.Run(async()=>await port.ClaimAsync(a,default)),Task.Run(async()=>await port.ClaimAsync(b,default)));
        Assert.Single(results.OfType<GlobalParticipantAllocatorClaimResultV1.Committed>());Assert.Single(results.OfType<GlobalParticipantAllocatorClaimResultV1.HeadConflict>());
    }

    [Fact] public async Task CanonicalSourceAndOutcomeContradictionsMapToHeadInvalid()
    {
        var (id,request)=Request();Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(request.ExactCanonicalRecordBytes,out var outer));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(),out var decoded));var x=decoded!;
        async Task AssertHeadInvalid(GlobalParticipantClaimRecordBodyV1 body)
        {var bytes=GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(outer.SourceSession,outer.SourceExpectedAuthority,GlobalParticipantAllocatorCodecsV1.Encode(body)));var r=Request(bytes,null);var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);Assert.Equal("head-invalid",Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(await port.ClaimAsync(r,default)).SafeCode.ToString());}
        var otherStamp=new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(31)),outer.SourceSession.LiveSessionId);var badSource=new GlobalParticipantAllocationSourceV1(outer.SourceSession.LiveSessionId,new JournalPositionV1(otherStamp,1),x.Source.SourceOuterIntegrityHash,x.Source.SourceBodyHash);var sourceParticipant=GlobalParticipantAllocatorFactIdsV1.Participant(outer.SourceSession.LiveSessionId,x.OperationId,GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(badSource));var sourceProof=new ParticipantIdOwnerProofV1(sourceParticipant,null,1,null,GlobalParticipantAllocatorCodecsV1.EmptyIndexRoot(),GlobalParticipantAllocatorCodecsV1.CreateEmptyProofPath(),0);await AssertHeadInvalid(new(x.OperationId,badSource,sourceParticipant,null,sourceProof,x.Outcome,x.AssignedPosition,x.ObservedAt));
        var conflict=new ParticipantIdClaimOutcomeV1(2,new GlobalParticipantAuthorityPositionV1(id,1),new BoundedAscii("participant-id-owned"));await AssertHeadInvalid(new(x.OperationId,x.Source,x.ParticipantId,null,x.OwnerProof,conflict,x.AssignedPosition,x.ObservedAt));
    }

    [Fact] public async Task ParticipantMismatchAndFactPrecedenceUseSelfConsistentProof()
    {
        var (id,request)=Request();Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(request.ExactCanonicalRecordBytes,out var outer));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(),out var decoded));var x=decoded!;var alternate=ParticipantId.FromValue(Id(44));var proof=new ParticipantIdOwnerProofV1(alternate,null,1,null,GlobalParticipantAllocatorCodecsV1.EmptyIndexRoot(),GlobalParticipantAllocatorCodecsV1.CreateEmptyProofPath(),0);var body=new GlobalParticipantClaimRecordBodyV1(x.OperationId,x.Source,alternate,null,proof,x.Outcome,x.AssignedPosition,x.ObservedAt);var bytes=GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(outer.SourceSession,outer.SourceExpectedAuthority,GlobalParticipantAllocatorCodecsV1.Encode(body)));var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);
        Assert.Equal("participant-id-mismatch",Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(await port.ClaimAsync(new(id,null,bytes,request.FactId),default)).SafeCode.ToString());
        Assert.Equal("fact-id-mismatch",Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(await port.ClaimAsync(new(id,null,bytes,default),default)).SafeCode.ToString());
    }

    [Fact] public async Task MaximumSizedNoncanonicalRecordPassesSizeAndFailsWire()
    {var id=Journal();var port=(IGlobalParticipantAllocatorClaimPortV1)new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);var result=Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(await port.ClaimAsync(new(id,null,new byte[8192],default),default));Assert.Equal("record-wire-invalid",result.SafeCode.ToString());}

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SnapshotIndependentlyQuarantinesRootOrOwnerCorruption(bool root)
    {var (id,request)=Request();var backend=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(id);Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await ((IGlobalParticipantAllocatorClaimPortV1)backend).ClaimAsync(request,default));var field=typeof(InMemoryGlobalParticipantAllocatorConformanceBackendV1).GetField("_state",BindingFlags.Instance|BindingFlags.NonPublic)!;var state=(InMemoryGlobalParticipantAllocatorConformanceBackendV1.State)field.GetValue(backend)!;var changed=root?state with{IndexRoot=Hash("wrong-root")}:state with{Owners=ImmutableDictionary<ParticipantId,ParticipantIdOwnerEvidenceV1>.Empty};field.SetValue(backend,changed);Assert.IsType<GlobalParticipantAllocatorSnapshotResultV1.Quarantined>(((IGlobalParticipantAllocatorExactRecordSnapshotReaderV1)backend).ReadExactSnapshot());Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Quarantined>(await ((IGlobalParticipantAllocatorClaimPortV1)backend).ClaimAsync(request,default));}

    [Fact] public async Task SelfConsistentProofForWrongRetainedRootMapsToHeadInvalid()
    {var pair=GlobalParticipantAllocatorFoldV1Tests.AppliedRecords2();var first=Request(pair.First,null);var backend=new InMemoryGlobalParticipantAllocatorConformanceBackendV1(first.JournalId);var port=(IGlobalParticipantAllocatorClaimPortV1)backend;var committed=Assert.IsType<GlobalParticipantAllocatorClaimResultV1.Committed>(await port.ClaimAsync(first,default));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(pair.Second,out var outer));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(),out var decoded));var x=decoded!;var wrong=new ParticipantIdOwnerProofV1(x.ParticipantId,committed.Head,1,null,GlobalParticipantAllocatorCodecsV1.EmptyIndexRoot(),GlobalParticipantAllocatorCodecsV1.CreateEmptyProofPath(),1);var body=new GlobalParticipantClaimRecordBodyV1(x.OperationId,x.Source,x.ParticipantId,committed.Head,wrong,x.Outcome,x.AssignedPosition,x.ObservedAt);var bytes=GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(outer.SourceSession,outer.SourceExpectedAuthority,GlobalParticipantAllocatorCodecsV1.Encode(body)));var result=Assert.IsType<GlobalParticipantAllocatorClaimResultV1.InvalidRecord>(await port.ClaimAsync(Request(bytes,committed.Head),default));Assert.Equal("head-invalid",result.SafeCode.ToString());}

    private static (GlobalParticipantAllocatorJournalId,GlobalParticipantAllocatorClaimRequestV1) Request()
    {var bytes=GlobalParticipantAllocatorFoldV1Tests.AppliedRecord();Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(bytes,out var outer));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(),out var body));var fingerprint=GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(body!.Source);var fact=GlobalParticipantAllocatorFactIdsV1.Fact(outer.SourceSession.LiveSessionId,body.OperationId,fingerprint);return(body.AssignedPosition.JournalId,new(body.AssignedPosition.JournalId,null,bytes,fact));}
    private static GlobalParticipantAllocatorClaimRequestV1 Request(byte[] bytes,GlobalParticipantAuthorityHeadV1? head)
    {Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeOuter(bytes,out var outer));Assert.True(GlobalParticipantAllocatorCodecsV1.TryDecodeBody(outer!.BodyBytes.ToArray(),out var body));var fingerprint=GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(body!.Source);var fact=GlobalParticipantAllocatorFactIdsV1.Fact(outer.SourceSession.LiveSessionId,body.OperationId,fingerprint);return new(body.AssignedPosition.JournalId,head,bytes,fact);}
    private static byte[] RejectedRecord(ulong sequence,GlobalParticipantAuthorityHeadV1? head,ulong salt=0)
    {var session=new SessionAuthorityStampV1(RuntimeGenerationId.FromValue(Id(1)),LiveSessionId.FromValue(Id(2)));Span<byte> raw=stackalloc byte[16];BinaryPrimitives.WriteUInt64BigEndian(raw[8..],sequence+salt);var operation=OperationId.FromValue(StableId128.FromBytes(raw));var source=new GlobalParticipantAllocationSourceV1(session.LiveSessionId,new JournalPositionV1(session,checked((long)(sequence+salt))),Hash("outer"),Hash("body"));var participant=GlobalParticipantAllocatorFactIdsV1.Participant(session.LiveSessionId,operation,GlobalParticipantAllocatorFactIdsV1.SourceFingerprint(source));var proof=new ParticipantIdOwnerProofV1(participant,head,1,null,GlobalParticipantAllocatorCodecsV1.EmptyIndexRoot(),GlobalParticipantAllocatorCodecsV1.CreateEmptyProofPath(),sequence-1);var body=new GlobalParticipantClaimRecordBodyV1(operation,source,participant,head,proof,new ParticipantIdClaimOutcomeV1(3,null,new BoundedAscii("invalid-body")),new GlobalParticipantAuthorityPositionV1(Journal(),sequence),new MonotonicStampV1(ClockDomainId.FromValue(Id(8)),BootId.FromValue(Id(9)),sequence));return GlobalParticipantAllocatorCodecsV1.Encode(new GlobalParticipantClaimRecordV1(session,ExpectedAuthorityVectorV1.Create(session,[]),GlobalParticipantAllocatorCodecsV1.Encode(body)));}
    private static GlobalParticipantAllocatorJournalId Journal()=>GlobalParticipantAllocatorJournalId.FromValue(Id(5));
    private static StableId128 Id(byte value)=>StableId128.FromBytes(Enumerable.Repeat(value,16).ToArray());
    private static Hash256 Hash(string value)=>Hash256.FromBytes(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
