using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal static class GraphParticipantReservationCodecsV2
{
    internal const ushort Major = 2;
    internal const ushort Minor = 0;
    internal const int MaximumReservationCommandBodyBytes = 16_384;
    internal const int MaximumReservationFactBodyBytes = 16_384;
    internal const int MaximumOuterBytes = 65_833;
    internal const string ReservationCommandSchemaId = "hpd.authority-payload-graph-participant-reservation-command.v2";
    internal const string ReservationFactSchemaId = "hpd.authority-payload-graph-participant-reservation-fact.v2";
    internal const string ReservationCommandBodySchemaId = "hpd.graph-participant-reservation-command-body.v2";
    internal const string ReservationFactBodySchemaId = "hpd.graph-participant-reservation-fact-body.v2";

    internal static byte[] Encode(GraphParticipantReservationCommandV2 value) => EncodeOuter(value.Session, value.ExpectedAuthority, value.BodyBytes,MaximumReservationCommandBodyBytes);
    internal static byte[] Encode(GraphParticipantReservationFactV2 value) => EncodeOuter(value.Session, value.ExpectedAuthority, value.BodyBytes,MaximumReservationFactBodyBytes);

    internal static byte[] Encode(GraphParticipantReservationCommandBodyV2 v)
    {
        var w = Writer(9); Tag(w,1); WriteId(w,v.OperationId); Tag(w,2); WritePositionOption(w,v.ExpectedReservationFact);
        Tag(w,3); WriteId(w,v.RuntimeGeneration); Tag(w,4); WriteId(w,v.GraphGeneration); Tag(w,5); WriteHash(w,v.ParticipantPlanFingerprint);
        Tag(w,6); WriteHash(w,v.AllocationCarrierFingerprint);
        Tag(w,7); WriteAscii128(w,v.ParticipantFactoryKey); Tag(w,8); WriteKeys(w,v.OrderedTopologyNodeKeys);
        Tag(w,9); WriteStamp(w,v.ObservedAt); return Finish(w);
    }

    internal static byte[] Encode(GraphParticipantReservationFactBodyV2 v)
    {
        if(v is null)throw new ArgumentException("Fact body is required.",nameof(v));ValidateOutcome(v.Outcome); ValidateReservationArms(v);
        var w=Writer(11); Tag(w,1); WriteId(w,v.OperationId); Tag(w,2); AuthorityPositionCodecsV1.Write(w,v.CommandPosition);
        Tag(w,3); WritePositionOption(w,v.ActualPredecessor); Tag(w,4); w.WriteUInt64(v.Outcome); Tag(w,5); WriteId(w,v.RuntimeGeneration); Tag(w,6); WriteId(w,v.GraphGeneration);
        Tag(w,7); WriteHash(w,v.ParticipantPlanFingerprint); Tag(w,8); WriteHash(w,v.AllocationCarrierFingerprint);
        Tag(w,9); WriteOptional(w,v.Reservation,WriteReservation); Tag(w,10); WriteSafeCode(w,v.SafeCode); Tag(w,11); WriteStamp(w,v.ObservedAt); return Finish(w);
    }

    internal static bool TryDecodeReservationCommand(ReadOnlyMemory<byte> bytes, out GraphParticipantReservationCommandV2? value) =>
        TryDecodeOuter(bytes, MaximumReservationCommandBodyBytes, static (s, a, b) => new(s, a, b), out value);
    internal static bool TryDecodeReservationFact(ReadOnlyMemory<byte> bytes, out GraphParticipantReservationFactV2? value) =>
        TryDecodeOuter(bytes, MaximumReservationFactBodyBytes, static (s, a, b) => new(s, a, b), out value);

    internal static bool TryDecodeReservationCommandBody(ReadOnlyMemory<byte> b, out GraphParticipantReservationCommandBodyV2? v) => TryBody(b,MaximumReservationCommandBodyBytes,ReadReservationCommand,Encode,out v);
    internal static bool TryDecodeReservationFactBody(ReadOnlyMemory<byte> b, out GraphParticipantReservationFactBodyV2? v) => TryBody(b,MaximumReservationFactBodyBytes,ReadReservationFact,Encode,out v);
    internal static Hash256 ComputeHash(GraphParticipantReservationCommandBodyV2 v)=>AuthorityIntegrityHashV1.Compute(ReservationCommandBodySchemaId,2,0,Encode(v));
    internal static Hash256 ComputeHash(GraphParticipantReservationFactBodyV2 v)=>AuthorityIntegrityHashV1.Compute(ReservationFactBodySchemaId,2,0,Encode(v));

    private static GraphParticipantReservationCommandBodyV2 ReadReservationCommand(CborReader r)
    {
        Start(r,9); Need(r,1); var op=ReadOperation(r); Need(r,2); var expected=ReadPositionOption(r); Need(r,3); var runtime=ReadRuntime(r);
        Need(r,4); var graph=ReadGraph(r); Need(r,5); var plan=ReadHash(r); Need(r,6); var allocation=ReadHash(r); Need(r,7); var factory=ReadAscii128(r);
        Need(r,8); var keys=ReadKeys(r); Need(r,9); var stamp=ReadStamp(r); r.ReadEndMap(); return new(op,expected,runtime,graph,plan,allocation,factory,keys,stamp);
    }
    private static GraphParticipantReservationFactBodyV2 ReadReservationFact(CborReader r)
    {
        Start(r,11); Need(r,1); var op=ReadOperation(r); Need(r,2); var command=AuthorityPositionCodecsV1.ReadJournal(r); Need(r,3); var predecessor=ReadPositionOption(r);
        Need(r,4); var outcome=ReadOutcome(r); Need(r,5); var runtime=ReadRuntime(r); Need(r,6); var graph=ReadGraph(r); Need(r,7); var plan=ReadHash(r);
        Need(r,8); var allocation=ReadHash(r); Need(r,9); var reservation=ReadOptional(r,ReadReservation); Need(r,10); var code=ReadSafeCode(r); Need(r,11); var stamp=ReadStamp(r); r.ReadEndMap();
        var v=new GraphParticipantReservationFactBodyV2(op,command,predecessor,outcome,runtime,graph,plan,allocation,reservation,code,stamp); ValidateReservationArms(v); return v;
    }
    private static byte[] EncodeOuter(SessionAuthorityStampV1 s, ExpectedAuthorityVectorV1 a, ReadOnlySpan<byte> body,int maximum)
    {
        if (!s.IsValid || a is null || a.Session != s || body.Length>maximum) throw new ArgumentException("Invalid graph-participant outer payload.");
        var w=Writer(3); Tag(w,1); SessionAuthorityStampV1Codec.Write(w,s); Tag(w,2); AuthorityVectorCodecsV1.WriteVector(w,a); Tag(w,3); w.WriteByteString(body); return Finish(w);
    }
    private static bool TryDecodeOuter<T>(ReadOnlyMemory<byte> b, int innerMaximum, Func<SessionAuthorityStampV1,ExpectedAuthorityVectorV1,byte[],T> make, out T? value) where T:class
    {
        value=null; if(b.Length>MaximumOuterBytes)return false; byte[]? buffer=null;
        try { var r=new CborReader(b,CborConformanceMode.Ctap2Canonical,false); Start(r,3); Need(r,1); var s=SessionAuthorityStampV1Codec.Read(r); Need(r,2); var a=AuthorityVectorCodecsV1.ReadVector(r); Need(r,3); var declared=ReadDeclaredByteStringLength(b.Span,b.Length-r.BytesRemaining); if(declared<0||declared>innerMaximum)return false; buffer=System.Buffers.ArrayPool<byte>.Shared.Rent(Math.Max(1,declared)); if(!r.TryReadByteString(buffer,out var length)||length!=declared)return false; r.ReadEndMap(); if(r.BytesRemaining!=0||a.Session!=s)return false; var body=buffer.AsSpan(0,length).ToArray(); value=make(s,a,body); return b.Span.SequenceEqual(EncodeOuter(s,a,body,innerMaximum)); }
        catch(Exception e) when(e is CborContentException or InvalidOperationException or ArgumentException or OverflowException){return false;}
        finally { if(buffer is not null)System.Buffers.ArrayPool<byte>.Shared.Return(buffer,clearArray:true); }
    }
    private static int ReadDeclaredByteStringLength(ReadOnlySpan<byte> bytes,int offset)
    {
        if((uint)offset>=(uint)bytes.Length||(bytes[offset]&0xe0)!=0x40)return -1; var info=bytes[offset]&0x1f;
        if(info<24)return info;
        if(info==24&&offset+1<bytes.Length&&bytes[offset+1]>=24)return bytes[offset+1];
        if(info==25&&offset+2<bytes.Length){var n=System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes[(offset+1)..]);return n>byte.MaxValue?n:-1;}
        if(info==26&&offset+4<bytes.Length){var n=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset+1)..]);return n>ushort.MaxValue&&n<=int.MaxValue?(int)n:-1;}
        return -1;
    }
    private static bool TryBody<T>(ReadOnlyMemory<byte> b,int max,Func<CborReader,T> read,Func<T,byte[]> encode,out T? value) where T:class
    {
        value=null; if(b.Length>max)return false;
        try { var r=new CborReader(b,CborConformanceMode.Ctap2Canonical,false); var candidate=read(r); if(r.BytesRemaining!=0||!b.Span.SequenceEqual(encode(candidate)))return false; value=candidate; return true; }
        catch(Exception e) when(e is CborContentException or InvalidOperationException or ArgumentException or OverflowException){return false;}
    }
    private static CborWriter Writer(int count){var w=new CborWriter(CborConformanceMode.Ctap2Canonical);w.WriteStartMap(count);return w;}
    private static byte[] Finish(CborWriter w){w.WriteEndMap();return w.Encode();}
    private static void Tag(CborWriter w,ulong tag)=>w.WriteUInt64(tag);
    private static void Start(CborReader r,int n){if(r.ReadStartMap()!=n)throw Bad();}
    private static void Need(CborReader r,ulong tag){if(r.ReadUInt64()!=tag)throw Bad();}
    private static ArgumentException Bad()=>new("Unexpected graph-participant field shape.");

    private static void WritePositionOption(CborWriter w,JournalPositionV1? p)=>WriteOptional(w,p,AuthorityPositionCodecsV1.Write);
    private static JournalPositionV1? ReadPositionOption(CborReader r)=>ReadOptional(r,AuthorityPositionCodecsV1.ReadJournal);
    private static void WriteOptional<T>(CborWriter w,T? value,Action<CborWriter,T> write) where T:class {w.WriteStartMap(value is null?1:2);Tag(w,1);w.WriteUInt64(value is null?0UL:1UL);if(value is not null){Tag(w,2);write(w,value);}w.WriteEndMap();}
    private static void WriteOptional(CborWriter w,JournalPositionV1? value,Action<CborWriter,JournalPositionV1> write){w.WriteStartMap(value is null?1:2);Tag(w,1);w.WriteUInt64(value is null?0UL:1UL);if(value is { } x){Tag(w,2);write(w,x);}w.WriteEndMap();}
    private static T? ReadOptional<T>(CborReader r,Func<CborReader,T> read) where T:class {var n=r.ReadStartMap();Need(r,1);var k=r.ReadUInt64();if(n==1&&k==0){r.ReadEndMap();return null;}if(n!=2||k!=1)throw Bad();Need(r,2);var v=read(r);r.ReadEndMap();return v;}
    private static JournalPositionV1? ReadOptional(CborReader r,Func<CborReader,JournalPositionV1> read){var n=r.ReadStartMap();Need(r,1);var k=r.ReadUInt64();if(n==1&&k==0){r.ReadEndMap();return null;}if(n!=2||k!=1)throw Bad();Need(r,2);var v=read(r);r.ReadEndMap();return v;}

    private static void WriteReservation(CborWriter w,GraphParticipantReservationV1 v){w.WriteStartMap(3);Tag(w,1);WriteId(w,v.ParticipantId);Tag(w,2);WriteAscii128(w,v.ParticipantFactoryKey);Tag(w,3);WriteKeys(w,v.OrderedTopologyNodeKeys);w.WriteEndMap();}
    private static GraphParticipantReservationV1 ReadReservation(CborReader r){Start(r,3);Need(r,1);var id=ReadParticipant(r);Need(r,2);var f=ReadAscii128(r);Need(r,3);var k=ReadKeys(r);r.ReadEndMap();return new(id,f,k);}
    private static void WriteKeys(CborWriter w,IReadOnlyList<BoundedAscii> keys){if(keys is null||keys.Count is <1 or >64)throw Bad();w.WriteStartArray(keys.Count);BoundedAscii prior=default;foreach(var key in keys){if(!key.IsValid||key.ToString().Length>128||(prior.IsValid&&prior.CompareTo(key)>=0))throw Bad();BoundedAsciiCodec.Write(w,key);prior=key;}w.WriteEndArray();}
    private static IReadOnlyList<BoundedAscii> ReadKeys(CborReader r){var n=r.ReadStartArray();if(n is null or <1 or >64)throw Bad();var a=new BoundedAscii[n.Value];BoundedAscii prior=default;for(var i=0;i<a.Length;i++){a[i]=ReadAscii128(r);if(prior.IsValid&&prior.CompareTo(a[i])>=0)throw Bad();prior=a[i];}r.ReadEndArray();return Array.AsReadOnly(a);}
    private static void WriteAscii128(CborWriter w,BoundedAscii v){if(!v.IsValid||v.ToString().Length>128)throw Bad();BoundedAsciiCodec.Write(w,v);}
    private static BoundedAscii ReadAscii128(CborReader r){var v=BoundedAsciiCodec.Read(r);if(v.ToString().Length>128)throw Bad();return v;}
    private static readonly HashSet<string> SafeCodes=new(["participant-already-reserved","reservation-predecessor-conflict","authority-stale","plan-fingerprint-mismatch","topology-node-set-mismatch","participant-factory-mismatch","participant-id-collision","invalid-body"],StringComparer.Ordinal);
    private static void WriteSafeCode(CborWriter w,BoundedAscii? v){if(v is { } x&&(!x.IsValid||x.ToString().Length>64||!SafeCodes.Contains(x.ToString())))throw Bad();WriteOptionalValue(w,v);}
    private static BoundedAscii? ReadSafeCode(CborReader r){var n=r.ReadStartMap();Need(r,1);var k=r.ReadUInt64();if(n==1&&k==0){r.ReadEndMap();return null;}if(n!=2||k!=1)throw Bad();Need(r,2);var v=BoundedAsciiCodec.Read(r);r.ReadEndMap();if(v.ToString().Length>64||!SafeCodes.Contains(v.ToString()))throw Bad();return v;}
    private static void WriteOptionalValue(CborWriter w,BoundedAscii? v){w.WriteStartMap(v is null?1:2);Tag(w,1);w.WriteUInt64(v is null?0UL:1UL);if(v is { } x){Tag(w,2);BoundedAsciiCodec.Write(w,x);}w.WriteEndMap();}
    private static void WriteStamp(CborWriter w,MonotonicStampV1 v)=>w.WriteEncodedValue(MonotonicStampV1Codec.Encode(v));
    private static MonotonicStampV1 ReadStamp(CborReader r){if(!MonotonicStampV1Codec.TryDecode(r.ReadEncodedValue(),out var v))throw Bad();return v;}
    private static void WriteHash(CborWriter w,Hash256 v){Span<byte>b=stackalloc byte[32];if(!v.TryWriteBytes(b))throw Bad();w.WriteByteString(b);}
    private static Hash256 ReadHash(CborReader r){Span<byte>b=stackalloc byte[32];if(!r.TryReadByteString(b,out var n)||n!=32)throw Bad();return Hash256.FromBytes(b);}
    private static void WriteId(CborWriter w,OperationId v){Span<byte>b=stackalloc byte[16];if(!v.TryWriteBytes(b))throw Bad();w.WriteByteString(b);}
    private static void WriteId(CborWriter w,RuntimeGenerationId v){Span<byte>b=stackalloc byte[16];if(!v.TryWriteBytes(b))throw Bad();w.WriteByteString(b);}
    private static void WriteId(CborWriter w,GraphGenerationId v){Span<byte>b=stackalloc byte[16];if(!v.TryWriteBytes(b))throw Bad();w.WriteByteString(b);}
    private static void WriteId(CborWriter w,ParticipantId v){Span<byte>b=stackalloc byte[16];if(!v.TryWriteBytes(b))throw Bad();w.WriteByteString(b);}
    private static StableId128 ReadStable(CborReader r){Span<byte>b=stackalloc byte[16];if(!r.TryReadByteString(b,out var n)||n!=16)throw Bad();return StableId128.FromBytes(b);}
    private static OperationId ReadOperation(CborReader r)=>OperationId.FromValue(ReadStable(r));
    private static RuntimeGenerationId ReadRuntime(CborReader r)=>RuntimeGenerationId.FromValue(ReadStable(r));
    private static GraphGenerationId ReadGraph(CborReader r)=>GraphGenerationId.FromValue(ReadStable(r));
    private static ParticipantId ReadParticipant(CborReader r)=>ParticipantId.FromValue(ReadStable(r));
    private static ushort ReadOutcome(CborReader r){var x=checked((ushort)r.ReadUInt64());ValidateOutcome(x);return x;}
    private static void ValidateOutcome(ushort x){if(x is not (1 or 2))throw Bad();}
    private static void ValidateReservationArms(GraphParticipantReservationFactBodyV2 v){if(v.Outcome==1?(v.Reservation is null||v.SafeCode is not null):(v.Reservation is not null||v.SafeCode is null))throw Bad();}
}
