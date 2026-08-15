using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal readonly record struct DecodedAuthorityLifecycleRecordV1(OperationId OperationId,
    JournalPositionV1 SourcePosition, ExpectedAuthorityVectorV1 Authority, ushort Disposition);

internal static class AuthorityLifecycleRecordCodecV1
{
    internal static byte[] Encode(OperationId operationId, JournalPositionV1 sourcePosition,
        ExpectedAuthorityVectorV1 authority, ushort disposition)
    {
        Validate(operationId, sourcePosition, authority, disposition);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(4);
        writer.WriteUInt64(1); WriteOperation(writer, operationId);
        writer.WriteUInt64(2); writer.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(sourcePosition));
        writer.WriteUInt64(3); writer.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(authority));
        writer.WriteUInt64(4); writer.WriteUInt64(disposition);
        writer.WriteEndMap();
        return writer.Encode();
    }

    internal static bool TryDecode(ReadOnlyMemory<byte> bytes, out DecodedAuthorityLifecycleRecordV1 value)
    {
        value = default;
        if (bytes.Length is 0 or > 16_384) return false;
        try
        {
            var reader = new CborReader(bytes, CborConformanceMode.Ctap2Canonical, false);
            if (reader.ReadStartMap() != 4 || reader.ReadUInt64() != 1) return false;
            var operation = ReadOperation(reader);
            if (reader.ReadUInt64() != 2) return false;
            var position = AuthorityPositionCodecsV1.ReadJournal(reader);
            if (reader.ReadUInt64() != 3 || !AuthorityVectorCodecsV1.TryDecodeVector(reader.ReadEncodedValue(), out var authority)) return false;
            if (reader.ReadUInt64() != 4) return false;
            var rawDisposition = reader.ReadUInt64();
            reader.ReadEndMap();
            if (reader.BytesRemaining != 0 || rawDisposition is 0 or > ushort.MaxValue) return false;
            Validate(operation, position, authority!, (ushort)rawDisposition);
            value = new(operation, position, authority!, (ushort)rawDisposition);
            return Encode(value.OperationId, value.SourcePosition, value.Authority, value.Disposition).AsSpan().SequenceEqual(bytes.Span);
        }
        catch (Exception exception) when (exception is CborContentException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
    }

    internal static void Validate(OperationId operationId, JournalPositionV1 sourcePosition,
        ExpectedAuthorityVectorV1 authority, ushort disposition)
    { if(!operationId.IsValid||!sourcePosition.IsValid||authority is null||authority.Session!=sourcePosition.Session||disposition==0) throw new ArgumentException("Invalid authority lifecycle record."); }
    private static void WriteOperation(CborWriter writer,OperationId value){Span<byte> bytes=stackalloc byte[16];if(!value.TryWriteBytes(bytes))throw new ArgumentException("An operation is required.");writer.WriteByteString(bytes);}
    private static OperationId ReadOperation(CborReader reader){Span<byte> bytes=stackalloc byte[16];if(!reader.TryReadByteString(bytes,out var written)||written!=16)throw new CborContentException("An operation identifier is exactly 16 bytes.");return OperationId.FromValue(StableId128.FromBytes(bytes));}
}

internal abstract record CoreLifecycleRecordV1
{
    protected CoreLifecycleRecordV1(OperationId operationId,JournalPositionV1 sourcePosition,ExpectedAuthorityVectorV1 authority,ushort disposition)
    {AuthorityLifecycleRecordCodecV1.Validate(operationId,sourcePosition,authority,disposition);OperationId=operationId;SourcePosition=sourcePosition;Authority=authority;Disposition=disposition;}
    internal OperationId OperationId{get;} internal JournalPositionV1 SourcePosition{get;} internal ExpectedAuthorityVectorV1 Authority{get;} internal ushort Disposition{get;}
}
internal sealed record SemanticAcceptanceBoundV1 : CoreLifecycleRecordV1 { internal SemanticAcceptanceBoundV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record SemanticReservationCreatedV1 : CoreLifecycleRecordV1 { internal SemanticReservationCreatedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }
internal sealed record AuthorityFactAdmittedV1 : CoreLifecycleRecordV1 { internal AuthorityFactAdmittedV1(OperationId o,JournalPositionV1 p,ExpectedAuthorityVectorV1 a,ushort d):base(o,p,a,d){} }

internal static class CoreLifecycleRecordCodecsV1
{
    internal static byte[] Encode(CoreLifecycleRecordV1 v)=>AuthorityLifecycleRecordCodecV1.Encode(v.OperationId,v.SourcePosition,v.Authority,v.Disposition);
    internal static bool TryDecodeAcceptance(ReadOnlyMemory<byte>b,out SemanticAcceptanceBoundV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeReservation(ReadOnlyMemory<byte>b,out SemanticReservationCreatedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static bool TryDecodeAdmitted(ReadOnlyMemory<byte>b,out AuthorityFactAdmittedV1? v)=>Decode(b,static x=>new(x.OperationId,x.SourcePosition,x.Authority,x.Disposition),out v);
    internal static Hash256 ComputeHash(SemanticAcceptanceBoundV1 v)=>AuthorityIntegrityHashV1.Compute("hpd.semantic-acceptance-bound.v1",1,0,Encode(v));
    internal static Hash256 ComputeHash(SemanticReservationCreatedV1 v)=>AuthorityIntegrityHashV1.Compute("hpd.semantic-reservation-created.v1",1,0,Encode(v));
    internal static Hash256 ComputeHash(AuthorityFactAdmittedV1 v)=>AuthorityIntegrityHashV1.Compute("hpd.authority-fact-admitted.v1",1,0,Encode(v));
    private static bool Decode<T>(ReadOnlyMemory<byte>b,Func<DecodedAuthorityLifecycleRecordV1,T> create,out T? value)where T:CoreLifecycleRecordV1{value=null;if(!AuthorityLifecycleRecordCodecV1.TryDecode(b,out var decoded))return false;var candidate=create(decoded);if(!Encode(candidate).AsSpan().SequenceEqual(b.Span))return false;value=candidate;return true;}
}
