using System.Buffers.Binary;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Authority;

internal interface IGlobalParticipantAllocatorPageSourceV1
{
    ValueTask<ReadOnlyMemory<byte>?> ReadPageAsync(GlobalParticipantAllocatorJournalId journalId, ushort pageOrdinal, CancellationToken cancellationToken);
}

internal abstract record GlobalParticipantAllocatorPageReadResultV1
{
    private GlobalParticipantAllocatorPageReadResultV1() { }
    internal sealed record Verified(GlobalParticipantAllocatorCompletedFoldV1 Fold,Hash256 FinalPageHash,ushort TotalPages,ulong TotalRecords,ulong TotalCanonicalRecordBytes):GlobalParticipantAllocatorPageReadResultV1;
    internal sealed record Empty(GlobalParticipantAllocatorJournalId JournalId,Hash256 IndexRoot,Hash256 FinalPageHash):GlobalParticipantAllocatorPageReadResultV1;
    internal sealed record Incomplete(BoundedAscii SafeCode,ushort NextPageOrdinal):GlobalParticipantAllocatorPageReadResultV1;
    internal sealed record Quarantined(BoundedAscii SafeCode,ushort LastVerifiedPageOrdinal,ulong LastVerifiedRecordSequence):GlobalParticipantAllocatorPageReadResultV1;
}

internal sealed class GlobalParticipantAllocatorPageReaderV1
{
    private readonly IGlobalParticipantAllocatorPageSourceV1 _source;
    internal GlobalParticipantAllocatorPageReaderV1(IGlobalParticipantAllocatorPageSourceV1 source)=>_source=source??throw new ArgumentNullException(nameof(source));

    internal async ValueTask<GlobalParticipantAllocatorPageReadResultV1> ReadAsync(GlobalParticipantAllocatorJournalId journalId,CancellationToken cancellationToken)
    {
        if(!journalId.IsValid)throw new ArgumentException("A valid journal ID is required.",nameof(journalId));
        var fold=GlobalParticipantAllocatorFoldV1.Create(journalId);GlobalParticipantPageV1? first=null;Hash256? previous=null;ushort verifiedPages=0;ulong verifiedRecords=0,verifiedBytes=0;
        for(var ordinal=1;ordinal<=GlobalParticipantPageV1.MaximumPages;ordinal++)
        {
            var requested=checked((ushort)ordinal);if(cancellationToken.IsCancellationRequested)return Incomplete("read-cancelled",requested);
            ReadOnlyMemory<byte>? bytes;try{bytes=await _source.ReadPageAsync(journalId,requested,cancellationToken).ConfigureAwait(false);}catch(OperationCanceledException exception)when(cancellationToken.IsCancellationRequested&&exception.CancellationToken==cancellationToken){return Incomplete("read-cancelled",requested);}
            if(bytes is null)return Incomplete("page-unavailable",requested);
            if(bytes.Value.Length is 0 or >GlobalParticipantPageV1.MaximumCanonicalBytes)return Quarantine("page-size-invalid",verifiedPages,verifiedRecords);
            var shell=Preparse(bytes.Value,journalId);if(shell is not null)return Quarantine(shell,verifiedPages,verifiedRecords);
            if(!GlobalParticipantPageCodecV1.TryDecode(bytes.Value,out var page)||page is null)return Quarantine("page-wire-invalid",verifiedPages,verifiedRecords);
            if(page.JournalId!=journalId)return Quarantine("journal-mismatch",verifiedPages,verifiedRecords);
            if(page.PageOrdinal!=requested)return Quarantine("ordinal-mismatch",verifiedPages,verifiedRecords);
            if(requested==1?page.PreviousPageHash is not null:page.PreviousPageHash!=previous)return Quarantine("previous-page-hash-mismatch",verifiedPages,verifiedRecords);
            if(first is null)first=page;else if(page.PinnedHead!=first.PinnedHead||page.IndexRoot!=first.IndexRoot||page.TotalPages!=first.TotalPages||page.TotalRecords!=first.TotalRecords||page.TotalCanonicalBytes!=first.TotalCanonicalBytes)return Quarantine("pinned-tuple-mismatch",verifiedPages,verifiedRecords);
            if(page.TotalPages>GlobalParticipantPageV1.MaximumPages||page.TotalRecords>GlobalParticipantPageV1.MaximumTotalRecords||page.TotalCanonicalBytes>GlobalParticipantPageV1.MaximumTotalCanonicalBytes)return Quarantine("lifetime-limit-invalid",verifiedPages,verifiedRecords);
            if(!GlobalParticipantPageCodecV1.TryDecodeRecordsField(page.RecordsBytes.ToArray(),out var records))return Quarantine("record-framing-invalid",verifiedPages,verifiedRecords);
            if(records.Count!=page.RecordCount||verifiedRecords+(ulong)records.Count>page.TotalRecords)return Quarantine("record-count-invalid",verifiedPages,verifiedRecords);
            foreach(var record in records){if(fold.Apply(record) is GlobalParticipantAllocatorFoldApplyResultV1.InvalidHistory)return Quarantine("fold-invalid",verifiedPages,verifiedRecords);verifiedRecords++;}
            verifiedPages=requested;verifiedBytes+=page.PageCanonicalRecordBytes;previous=GlobalParticipantPageCodecV1.ComputePageHash(page);
            if(verifiedBytes>page.TotalCanonicalBytes)return Quarantine("record-byte-total-invalid",verifiedPages,verifiedRecords);
            if(page.IsFinal==0)continue;
            if(page.TotalPages!=requested||verifiedRecords!=page.TotalRecords||verifiedBytes!=page.TotalCanonicalBytes)return Quarantine("final-closure-invalid",verifiedPages,verifiedRecords);
            if(fold.Complete() is not GlobalParticipantAllocatorFoldResultV1.Current current||current.Snapshot.Head!=page.PinnedHead||current.Snapshot.IndexRoot!=page.IndexRoot)return Quarantine("final-closure-invalid",verifiedPages,verifiedRecords);
            if(verifiedRecords==0)return new GlobalParticipantAllocatorPageReadResultV1.Empty(journalId,page.IndexRoot,previous.Value);
            return new GlobalParticipantAllocatorPageReadResultV1.Verified(current.Snapshot,previous.Value,requested,verifiedRecords,verifiedBytes);
        }
        return Quarantine("lifetime-limit-invalid",verifiedPages,verifiedRecords);
    }
    private static GlobalParticipantAllocatorPageReadResultV1.Incomplete Incomplete(string code,ushort ordinal)=>new(new BoundedAscii(code),ordinal);
    private static GlobalParticipantAllocatorPageReadResultV1.Quarantined Quarantine(string code,ushort pages,ulong records)=>new(new BoundedAscii(code),pages,records);
    private static string? Preparse(ReadOnlyMemory<byte> bytes,GlobalParticipantAllocatorJournalId expected)
    {
        try
        {
            var r=new CborReader(bytes,CborConformanceMode.Ctap2Canonical,false);if(r.ReadStartMap()!=10)return "page-wire-invalid";
            if(r.ReadUInt64()!=1)return "page-wire-invalid";Span<byte> id=stackalloc byte[16];if(!r.TryReadByteString(id,out var n)||n!=16)return "page-wire-invalid";Span<byte> wanted=stackalloc byte[16];if(!expected.TryWriteBytes(wanted))return "page-wire-invalid";var journalMismatch=!id.SequenceEqual(wanted);
            for(ulong tag=2;tag<=5;tag++){if(r.ReadUInt64()!=tag)return "page-wire-invalid";r.SkipValue();}
            if(r.ReadUInt64()!=6)return "page-wire-invalid";var encodedRecords=r.ReadEncodedValue();if(!TryBstrSlice(encodedRecords,out var records))return "record-framing-invalid";
            if(r.ReadUInt64()!=7)return "page-wire-invalid";var final=r.ReadUInt64();if(r.ReadUInt64()!=8)return "page-wire-invalid";var pages=r.ReadUInt64();if(r.ReadUInt64()!=9)return "page-wire-invalid";var totalRecords=r.ReadUInt64();if(r.ReadUInt64()!=10)return "page-wire-invalid";var totalBytes=r.ReadUInt64();r.ReadEndMap();if(r.BytesRemaining!=0)return "page-wire-invalid";
            if(journalMismatch)return "journal-mismatch";
            var span=records.Span;if(span.Length is <2 or >GlobalParticipantPageV1.MaximumCanonicalBytes)return "record-framing-invalid";var count=BinaryPrimitives.ReadUInt16BigEndian(span);if(count>GlobalParticipantPageV1.MaximumRecordsPerPage)return "record-framing-invalid";var offset=2;ulong sum=0;for(var i=0;i<count;i++){if(span.Length-offset<4)return "record-framing-invalid";var length=BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset,4));offset+=4;if(length is 0 or >GlobalParticipantPageV1.MaximumRecordBytes||length>(uint)(span.Length-offset))return "record-framing-invalid";offset+=(int)length;sum+=length;}if(offset!=span.Length)return "record-framing-invalid";if((ulong)count>totalRecords)return "record-count-invalid";if(sum>totalBytes)return "record-byte-total-invalid";
            if(final>1||pages is 0 or >GlobalParticipantPageV1.MaximumPages||totalRecords>GlobalParticipantPageV1.MaximumTotalRecords||totalBytes>GlobalParticipantPageV1.MaximumTotalCanonicalBytes)return "lifetime-limit-invalid";return null;
        }
        catch(Exception e)when(e is CborContentException or InvalidOperationException or OverflowException or ArgumentException){return "page-wire-invalid";}
    }
    private static bool TryBstrSlice(ReadOnlyMemory<byte> encoded,out ReadOnlyMemory<byte> payload)
    {
        payload=default;var s=encoded.Span;if(s.Length==0||(s[0]>>5)!=2)return false;var ai=s[0]&31;int header;ulong length;if(ai<24){header=1;length=(ulong)ai;}else if(ai==24&&s.Length>=2){header=2;length=s[1];if(length<24)return false;}else if(ai==25&&s.Length>=3){header=3;length=BinaryPrimitives.ReadUInt16BigEndian(s.Slice(1,2));if(length<=byte.MaxValue)return false;}else if(ai==26&&s.Length>=5){header=5;length=BinaryPrimitives.ReadUInt32BigEndian(s.Slice(1,4));if(length<=ushort.MaxValue)return false;}else if(ai==27&&s.Length>=9){header=9;length=BinaryPrimitives.ReadUInt64BigEndian(s.Slice(1,8));if(length<=uint.MaxValue)return false;}else return false;if(length>(ulong)(s.Length-header)||(ulong)header+length!=(ulong)s.Length)return false;payload=encoded.Slice(header,checked((int)length));return true;
    }
}
