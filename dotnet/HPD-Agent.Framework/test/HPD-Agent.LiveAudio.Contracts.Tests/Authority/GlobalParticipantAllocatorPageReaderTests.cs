using HPD.Agent.Audio.Authority;
using HPD.Agent.Authority;
using System.Formats.Cbor;

namespace HPD.Agent.LiveAudio.Contracts.Tests.Authority;

public sealed class GlobalParticipantAllocatorPageReaderTests
{
    [Fact] public async Task ExactEmptyPageReturnsEmpty()
    {var j=Journal();var page=new GlobalParticipantPage(j,null,GlobalParticipantPageCodec.DefaultIndexRoot,1,null,GlobalParticipantPageCodec.EncodeRecordsField([]),1,1,0,0);var reader=new GlobalParticipantAllocatorPageReader(new Source(GlobalParticipantPageCodec.Encode(page)));Assert.IsType<GlobalParticipantAllocatorPageReadResult.Empty>(await reader.ReadAsync(j,default));}

    [Fact] public async Task MissingWholePageIsIncompleteButTruncatedBytesQuarantine()
    {var j=Journal();var missing=await new GlobalParticipantAllocatorPageReader(new Source(null)).ReadAsync(j,default);Assert.Equal("page-unavailable",Assert.IsType<GlobalParticipantAllocatorPageReadResult.Incomplete>(missing).SafeCode.ToString());var truncated=await new GlobalParticipantAllocatorPageReader(new Source(new byte[]{0xa1})).ReadAsync(j,default);Assert.Equal("page-wire-invalid",Assert.IsType<GlobalParticipantAllocatorPageReadResult.Quarantined>(truncated).SafeCode.ToString());}

    [Theory][InlineData(0,"page-size-invalid")][InlineData(65537,"page-size-invalid")][InlineData(1,"page-wire-invalid")] public async Task PageSizeAndWireMutationMatrix(int length,string code)
    {var bytes=new byte[length];if(length==1)bytes[0]=0xff;Assert.Equal(code,Code(await Read(bytes)));}

    [Theory][InlineData(257UL,0UL,"record-framing-invalid")][InlineData(0UL,536870913UL,"lifetime-limit-invalid")] public async Task DeclaredMaximumPlusOneMatrix(ulong records,ulong bytes,string code)
    {var field=records==257?new byte[]{1,1}:new byte[]{0,0};Assert.Equal(code,Code(await Read(Shell(field,1,records,bytes))));}

    [Fact] public async Task CallerCancellationIsIncomplete()
    {using var cts=new CancellationTokenSource();cts.Cancel();var result=await new GlobalParticipantAllocatorPageReader(new Source(null)).ReadAsync(Journal(),cts.Token);Assert.Equal("read-cancelled",Assert.IsType<GlobalParticipantAllocatorPageReadResult.Incomplete>(result).SafeCode.ToString());}

    [Fact] public async Task OneRecordCompletePageReturnsVerifiedAndPinnedQuery()
    {var j=Journal();var record=GlobalParticipantAllocatorFoldV1Tests.AppliedRecord();var fold=GlobalParticipantAllocatorFoldV1.Create(j);var accepted=Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(fold.Apply(record));var snapshot=Assert.IsType<GlobalParticipantAllocatorFoldResultV1.Current>(fold.Complete()).Snapshot;var field=GlobalParticipantPageCodec.EncodeRecordsField([record]);var page=new GlobalParticipantPage(j,accepted.Head,snapshot.IndexRoot,1,null,field,1,1,1,(ulong)record.Length);var result=Assert.IsType<GlobalParticipantAllocatorPageReadResult.Verified>(await new GlobalParticipantAllocatorPageReader(new Source(GlobalParticipantPageCodec.Encode(page))).ReadAsync(j,default));Assert.Equal(1UL,result.TotalRecords);Assert.Equal((ushort)2,result.Fold.Query(GlobalParticipantAllocatorFoldV1Tests.ParticipantForRecord()).State);}

    [Fact] public async Task UnrelatedCancellationExceptionPropagates()
    {using var caller=new CancellationTokenSource();using var other=new CancellationTokenSource();other.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(async()=>await new GlobalParticipantAllocatorPageReader(new ThrowingSource(other.Token)).ReadAsync(Journal(),caller.Token));}

    [Fact] public async Task ShellCompoundErrorsUseFramingThenCountThenBytesThenLifetimePrecedence()
    {var malformed=Shell(new byte[]{0,1,0,0},2,70000,ulong.MaxValue);Assert.Equal("record-framing-invalid",Code(await Read(malformed)));var count=Shell(new byte[]{0,1,0,0,0,1,0xa0},2,0,ulong.MaxValue);Assert.Equal("record-count-invalid",Code(await Read(count)));var bytes=Shell(new byte[]{0,0},2,0,ulong.MaxValue);Assert.Equal("lifetime-limit-invalid",Code(await Read(bytes)));}

    [Fact] public async Task MalformedStructureWinsEvenWhenJournalBytesMismatch()
    {var page=Shell(new byte[]{0,0},1,0,0,wrongJournal:true);Array.Resize(ref page,page.Length-1);Assert.Equal("page-wire-invalid",Code(await Read(page)));}

    [Fact] public async Task SameSignalledTokenCancellationReturnsIncomplete()
    {using var cts=new CancellationTokenSource();cts.Cancel();var result=await new GlobalParticipantAllocatorPageReader(new ThrowingSource(cts.Token)).ReadAsync(Journal(),cts.Token);Assert.Equal("read-cancelled",Assert.IsType<GlobalParticipantAllocatorPageReadResult.Incomplete>(result).SafeCode.ToString());}

    [Fact] public async Task StructuralRecordCountBoundaryAccepts256FramingAndRejects257()
    {var atLimit=new byte[2+256*3];atLimit[0]=1;for(var i=0;i<256;i++){var p=2+i*3;atLimit[p+1]=1;atLimit[p+2]=0xa0;}Assert.IsType<GlobalParticipantAllocatorPageReadResult.Quarantined>(await Read(Shell(atLimit,1,256,256)));var over=new byte[2];over[0]=1;over[1]=1;Assert.Equal("record-framing-invalid",Code(await Read(Shell(over,1,257,0))));}

    [Fact] public async Task ValidTwoRecordSinglePageCompletes()
    {var j=Journal();var (a,b)=GlobalParticipantAllocatorFoldV1Tests.AppliedRecords2();var fold=GlobalParticipantAllocatorFoldV1.Create(j);fold.Apply(a);var accepted=Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(fold.Apply(b));var snap=Assert.IsType<GlobalParticipantAllocatorFoldResultV1.Current>(fold.Complete()).Snapshot;var page=new GlobalParticipantPage(j,accepted.Head,snap.IndexRoot,1,null,GlobalParticipantPageCodec.EncodeRecordsField([a,b]),1,1,2,(ulong)(a.Length+b.Length));var result=await new GlobalParticipantAllocatorPageReader(new Source(GlobalParticipantPageCodec.Encode(page))).ReadAsync(j,default);Assert.IsType<GlobalParticipantAllocatorPageReadResult.Verified>(result);}

    [Fact] public async Task ValidTwoPageChainCompletes()
    {var j=Journal();var (a,b)=GlobalParticipantAllocatorFoldV1Tests.AppliedRecords2();var fold=GlobalParticipantAllocatorFoldV1.Create(j);fold.Apply(a);var accepted=Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(fold.Apply(b));var snap=Assert.IsType<GlobalParticipantAllocatorFoldResultV1.Current>(fold.Complete()).Snapshot;var p1=new GlobalParticipantPage(j,accepted.Head,snap.IndexRoot,1,null,GlobalParticipantPageCodec.EncodeRecordsField([a]),0,2,2,(ulong)(a.Length+b.Length));var p1bytes=GlobalParticipantPageCodec.Encode(p1);var p2=new GlobalParticipantPage(j,accepted.Head,snap.IndexRoot,2,GlobalParticipantPageCodec.ComputePageHash(p1),GlobalParticipantPageCodec.EncodeRecordsField([b]),1,2,2,(ulong)(a.Length+b.Length));var result=await new GlobalParticipantAllocatorPageReader(new Pages(p1bytes,GlobalParticipantPageCodec.Encode(p2))).ReadAsync(j,default);Assert.IsType<GlobalParticipantAllocatorPageReadResult.Verified>(result);}

    [Fact] public async Task FinalPinnedRootMismatchUsesFinalClosureCode()
    {var j=Journal();var record=GlobalParticipantAllocatorFoldV1Tests.AppliedRecord();var fold=GlobalParticipantAllocatorFoldV1.Create(j);var accepted=Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(fold.Apply(record));var wrong=Hash256.FromBytes(Enumerable.Repeat((byte)9,32).ToArray());var page=new GlobalParticipantPage(j,accepted.Head,wrong,1,null,GlobalParticipantPageCodec.EncodeRecordsField([record]),1,1,1,(ulong)record.Length);Assert.Equal("final-closure-invalid",Code(await Read(GlobalParticipantPageCodec.Encode(page))));}

    [Fact] public async Task CanonicalSecondOrdinalDeliveredFirstReachesOrdinalMismatch()
    {var j=Journal();var (a,b)=GlobalParticipantAllocatorFoldV1Tests.AppliedRecords2();var fold=GlobalParticipantAllocatorFoldV1.Create(j);fold.Apply(a);var accepted=Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(fold.Apply(b));var snap=Assert.IsType<GlobalParticipantAllocatorFoldResultV1.Current>(fold.Complete()).Snapshot;var p1=new GlobalParticipantPage(j,accepted.Head,snap.IndexRoot,1,null,GlobalParticipantPageCodec.EncodeRecordsField([a]),0,2,2,(ulong)(a.Length+b.Length));var p2=new GlobalParticipantPage(j,accepted.Head,snap.IndexRoot,2,GlobalParticipantPageCodec.ComputePageHash(p1),GlobalParticipantPageCodec.EncodeRecordsField([b]),1,2,2,(ulong)(a.Length+b.Length));Assert.Equal("ordinal-mismatch",Code(await Read(GlobalParticipantPageCodec.Encode(p2))));}

    [Theory][InlineData(false,"previous-page-hash-mismatch")][InlineData(true,"pinned-tuple-mismatch")] public async Task SecondPageLinkAndTupleMutationsUseExactCodes(bool tuple,string expected)
    {var j=Journal();var (a,b)=GlobalParticipantAllocatorFoldV1Tests.AppliedRecords2();var fold=GlobalParticipantAllocatorFoldV1.Create(j);fold.Apply(a);var accepted=Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(fold.Apply(b));var snap=Assert.IsType<GlobalParticipantAllocatorFoldResultV1.Current>(fold.Complete()).Snapshot;var p1=new GlobalParticipantPage(j,accepted.Head,snap.IndexRoot,1,null,GlobalParticipantPageCodec.EncodeRecordsField([a]),0,2,2,(ulong)(a.Length+b.Length));var link=tuple?GlobalParticipantPageCodec.ComputePageHash(p1):Hash256.FromBytes(Enumerable.Repeat((byte)8,32).ToArray());var root=tuple?Hash256.FromBytes(Enumerable.Repeat((byte)9,32).ToArray()):snap.IndexRoot;var p2=new GlobalParticipantPage(j,accepted.Head,root,2,link,GlobalParticipantPageCodec.EncodeRecordsField([b]),1,2,2,(ulong)(a.Length+b.Length));Assert.Equal(expected,Code(await new GlobalParticipantAllocatorPageReader(new Pages(GlobalParticipantPageCodec.Encode(p1),GlobalParticipantPageCodec.Encode(p2))).ReadAsync(j,default)));}

    [Fact] public async Task DuplicateSecondRecordQuarantinesFoldAfterOneVerifiedRecord()
    {var j=Journal();var (a,b)=GlobalParticipantAllocatorFoldV1Tests.AppliedRecords2();var fold=GlobalParticipantAllocatorFoldV1.Create(j);fold.Apply(a);var accepted=Assert.IsType<GlobalParticipantAllocatorFoldApplyResultV1.Accepted>(fold.Apply(b));var snap=Assert.IsType<GlobalParticipantAllocatorFoldResultV1.Current>(fold.Complete()).Snapshot;var page=new GlobalParticipantPage(j,accepted.Head,snap.IndexRoot,1,null,GlobalParticipantPageCodec.EncodeRecordsField([a,a]),1,1,2,(ulong)(a.Length*2));var q=Assert.IsType<GlobalParticipantAllocatorPageReadResult.Quarantined>(await Read(GlobalParticipantPageCodec.Encode(page)));Assert.Equal("fold-invalid",q.SafeCode.ToString());Assert.Equal(1UL,q.LastVerifiedRecordSequence);}


    private sealed class Source(ReadOnlyMemory<byte>? page):IGlobalParticipantAllocatorPageSource
    {public ValueTask<ReadOnlyMemory<byte>?> ReadPageAsync(GlobalParticipantAllocatorJournalId journalId,ushort pageOrdinal,CancellationToken cancellationToken)=>ValueTask.FromResult(pageOrdinal==1?page:null);}
    private sealed class Pages(ReadOnlyMemory<byte> first,ReadOnlyMemory<byte> second):IGlobalParticipantAllocatorPageSource{public ValueTask<ReadOnlyMemory<byte>?> ReadPageAsync(GlobalParticipantAllocatorJournalId journalId,ushort pageOrdinal,CancellationToken cancellationToken)=>ValueTask.FromResult<ReadOnlyMemory<byte>?>(pageOrdinal==1?first:pageOrdinal==2?second:null);}
    private sealed class ThrowingSource(CancellationToken token):IGlobalParticipantAllocatorPageSource{public ValueTask<ReadOnlyMemory<byte>?> ReadPageAsync(GlobalParticipantAllocatorJournalId journalId,ushort pageOrdinal,CancellationToken cancellationToken)=>ValueTask.FromException<ReadOnlyMemory<byte>?>(new OperationCanceledException(token));}
    private static async Task<GlobalParticipantAllocatorPageReadResult> Read(byte[] page)=>await new GlobalParticipantAllocatorPageReader(new Source(page)).ReadAsync(Journal(),default);
    private static string Code(GlobalParticipantAllocatorPageReadResult value)=>Assert.IsType<GlobalParticipantAllocatorPageReadResult.Quarantined>(value).SafeCode.ToString();
    private static byte[] Shell(byte[] records,ulong pages,ulong totalRecords,ulong totalBytes,bool wrongJournal=false,ushort ordinal=1,bool previous=false){var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(10);w.WriteUInt64(1);Span<byte>id=stackalloc byte[16];Journal().TryWriteBytes(id);if(wrongJournal)id[0]^=0xff;w.WriteByteString(id);w.WriteUInt64(2);w.WriteStartMap(1);w.WriteUInt64(1);w.WriteUInt64(0);w.WriteEndMap();w.WriteUInt64(3);w.WriteByteString(new byte[32]);w.WriteUInt64(4);w.WriteUInt64(ordinal);w.WriteUInt64(5);w.WriteStartMap(previous?2:1);w.WriteUInt64(1);w.WriteUInt64(previous?1UL:0UL);if(previous){w.WriteUInt64(2);w.WriteByteString(new byte[32]);}w.WriteEndMap();w.WriteUInt64(6);w.WriteByteString(records);w.WriteUInt64(7);w.WriteUInt64(1);w.WriteUInt64(8);w.WriteUInt64(pages);w.WriteUInt64(9);w.WriteUInt64(totalRecords);w.WriteUInt64(10);w.WriteUInt64(totalBytes);w.WriteEndMap();return w.Encode();}
    private static GlobalParticipantAllocatorJournalId Journal()=>GlobalParticipantAllocatorJournalId.FromValue(StableId128.FromBytes(Enumerable.Repeat((byte)5,16).ToArray()));
}
