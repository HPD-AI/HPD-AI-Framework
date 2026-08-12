using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal static class GraphParticipantBindingCodecsV1
{
    internal const ushort Major = 1;
    internal const ushort Minor = 0;
    internal const int MaximumReservationCommandBodyBytes = 16_384;
    internal const int MaximumReservationFactBodyBytes = 16_384;
    internal const int MaximumBindingCommandBodyBytes = 4_096;
    internal const int MaximumBindingFactBodyBytes = 16_384;
    internal const int MaximumOuterBytes = 65_833;
    internal const string ReservationCommandSchemaId = "hpd.authority-payload-graph-participant-reservation-command.v1";
    internal const string ReservationFactSchemaId = "hpd.authority-payload-graph-participant-reservation-fact.v1";
    internal const string BindingCommandSchemaId = "hpd.authority-payload-graph-participant-binding-command.v1";
    internal const string BindingFactSchemaId = "hpd.authority-payload-graph-participant-binding-fact.v1";

    internal static byte[] Encode(GraphParticipantReservationCommandV1 value) => EncodeOuter(value.Session, value.ExpectedAuthority, value.BodyBytes,MaximumReservationCommandBodyBytes);
    internal static byte[] Encode(GraphParticipantReservationFactV1 value) => EncodeOuter(value.Session, value.ExpectedAuthority, value.BodyBytes,MaximumReservationFactBodyBytes);
    internal static byte[] Encode(GraphParticipantBindingCommandV1 value) => EncodeOuter(value.Session, value.ExpectedAuthority, value.BodyBytes,MaximumBindingCommandBodyBytes);
    internal static byte[] Encode(GraphParticipantBindingFactV1 value) => EncodeOuter(value.Session, value.ExpectedAuthority, value.BodyBytes,MaximumBindingFactBodyBytes);

    internal static bool TryDecodeReservationCommand(ReadOnlyMemory<byte> bytes, out GraphParticipantReservationCommandV1? value) =>
        TryDecodeOuter(bytes, MaximumReservationCommandBodyBytes, static (s, a, b) => new(s, a, b), out value);
    internal static bool TryDecodeReservationFact(ReadOnlyMemory<byte> bytes, out GraphParticipantReservationFactV1? value) =>
        TryDecodeOuter(bytes, MaximumReservationFactBodyBytes, static (s, a, b) => new(s, a, b), out value);
    internal static bool TryDecodeBindingCommand(ReadOnlyMemory<byte> bytes, out GraphParticipantBindingCommandV1? value) =>
        TryDecodeOuter(bytes, MaximumBindingCommandBodyBytes, static (s, a, b) => new(s, a, b), out value);
    internal static bool TryDecodeBindingFact(ReadOnlyMemory<byte> bytes, out GraphParticipantBindingFactV1? value) =>
        TryDecodeOuter(bytes, MaximumBindingFactBodyBytes, static (s, a, b) => new(s, a, b), out value);

    internal static byte[] Encode(GraphParticipantReservationCommandBodyV1 v)
    {
        var w = Writer(9); Tag(w,1); WriteId(w,v.OperationId); Tag(w,2); WritePositionOption(w,v.ExpectedReservationFact);
        Tag(w,3); WriteId(w,v.RuntimeGeneration); Tag(w,4); WriteHash(w,v.ParticipantPlanFingerprint);
        Tag(w,5); WriteHash(w,v.TopologyFingerprint); Tag(w,6); WriteHash(w,v.ExecutablePlanFingerprint);
        Tag(w,7); WriteAscii128(w,v.ParticipantFactoryKey); Tag(w,8); WriteKeys(w,v.OrderedTopologyNodeKeys);
        Tag(w,9); WriteStamp(w,v.ObservedAt); return Finish(w);
    }

    internal static byte[] Encode(GraphParticipantReservationFactBodyV1 v)
    {
        ValidateOutcome(v.Outcome); ValidateReservationArms(v);
        var w=Writer(11); Tag(w,1); WriteId(w,v.OperationId); Tag(w,2); AuthorityPositionCodecsV1.Write(w,v.CommandPosition);
        Tag(w,3); WritePositionOption(w,v.ActualPredecessor); Tag(w,4); w.WriteUInt64(v.Outcome); Tag(w,5); WriteId(w,v.RuntimeGeneration);
        Tag(w,6); WriteHash(w,v.ParticipantPlanFingerprint); Tag(w,7); WriteHash(w,v.TopologyFingerprint); Tag(w,8); WriteHash(w,v.ExecutablePlanFingerprint);
        Tag(w,9); WriteOptional(w,v.Reservation,WriteReservation); Tag(w,10); WriteSafeCode(w,v.SafeCode); Tag(w,11); WriteStamp(w,v.ObservedAt); return Finish(w);
    }

    internal static byte[] Encode(GraphParticipantBindingCommandBodyV1 v)
    {
        var w=Writer(10); Tag(w,1); WriteId(w,v.OperationId); Tag(w,2); AuthorityPositionCodecsV1.Write(w,v.ReservationFact);
        Tag(w,3); WritePositionOption(w,v.ExpectedBindingFact); Tag(w,4); WriteId(w,v.GraphGeneration); Tag(w,5); WriteId(w,v.RuntimeGeneration);
        Tag(w,6); WriteHash(w,v.ParticipantPlanFingerprint); Tag(w,7); WriteHash(w,v.TopologyFingerprint); Tag(w,8); WriteHash(w,v.ExecutablePlanFingerprint);
        Tag(w,9); WriteProof(w,v.CapacityGrantProof); Tag(w,10); WriteStamp(w,v.ObservedAt); return Finish(w);
    }

    internal static byte[] Encode(GraphParticipantBindingFactBodyV1 v)
    {
        ValidateOutcome(v.Outcome); ValidateBindingArms(v);
        var w=Writer(14); Tag(w,1); WriteId(w,v.OperationId); Tag(w,2); AuthorityPositionCodecsV1.Write(w,v.CommandPosition);
        Tag(w,3); AuthorityPositionCodecsV1.Write(w,v.ReservationFact); Tag(w,4); WritePositionOption(w,v.ActualPredecessor); Tag(w,5); w.WriteUInt64(v.Outcome);
        Tag(w,6); WriteId(w,v.GraphGeneration); Tag(w,7); WriteId(w,v.RuntimeGeneration); Tag(w,8); WriteHash(w,v.ParticipantPlanFingerprint);
        Tag(w,9); WriteHash(w,v.TopologyFingerprint); Tag(w,10); WriteHash(w,v.ExecutablePlanFingerprint); Tag(w,11); WriteOptional(w,v.Binding,WriteBinding);
        Tag(w,12); WriteOptional(w,v.CapacityGrantProof,WriteProof); Tag(w,13); WriteSafeCode(w,v.SafeCode); Tag(w,14); WriteStamp(w,v.ObservedAt); return Finish(w);
    }

    internal static bool TryDecodeReservationCommandBody(ReadOnlyMemory<byte> b, out GraphParticipantReservationCommandBodyV1? v) => TryBody(b,MaximumReservationCommandBodyBytes,ReadReservationCommand,Encode,out v);
    internal static bool TryDecodeReservationFactBody(ReadOnlyMemory<byte> b, out GraphParticipantReservationFactBodyV1? v) => TryBody(b,MaximumReservationFactBodyBytes,ReadReservationFact,Encode,out v);
    internal static bool TryDecodeBindingCommandBody(ReadOnlyMemory<byte> b, out GraphParticipantBindingCommandBodyV1? v) => TryBody(b,MaximumBindingCommandBodyBytes,ReadBindingCommand,Encode,out v);
    internal static bool TryDecodeBindingFactBody(ReadOnlyMemory<byte> b, out GraphParticipantBindingFactBodyV1? v) => TryBody(b,MaximumBindingFactBodyBytes,ReadBindingFact,Encode,out v);

    private static GraphParticipantReservationCommandBodyV1 ReadReservationCommand(CborReader r)
    {
        Start(r,9); Need(r,1); var op=ReadOperation(r); Need(r,2); var expected=ReadPositionOption(r); Need(r,3); var runtime=ReadRuntime(r);
        Need(r,4); var plan=ReadHash(r); Need(r,5); var topology=ReadHash(r); Need(r,6); var executable=ReadHash(r); Need(r,7); var factory=ReadAscii128(r);
        Need(r,8); var keys=ReadKeys(r); Need(r,9); var stamp=ReadStamp(r); r.ReadEndMap(); return new(op,expected,runtime,plan,topology,executable,factory,keys,stamp);
    }
    private static GraphParticipantReservationFactBodyV1 ReadReservationFact(CborReader r)
    {
        Start(r,11); Need(r,1); var op=ReadOperation(r); Need(r,2); var command=AuthorityPositionCodecsV1.ReadJournal(r); Need(r,3); var predecessor=ReadPositionOption(r);
        Need(r,4); var outcome=ReadOutcome(r); Need(r,5); var runtime=ReadRuntime(r); Need(r,6); var plan=ReadHash(r); Need(r,7); var topology=ReadHash(r);
        Need(r,8); var executable=ReadHash(r); Need(r,9); var reservation=ReadOptional(r,ReadReservation); Need(r,10); var code=ReadSafeCode(r); Need(r,11); var stamp=ReadStamp(r); r.ReadEndMap();
        var v=new GraphParticipantReservationFactBodyV1(op,command,predecessor,outcome,runtime,plan,topology,executable,reservation,code,stamp); ValidateReservationArms(v); return v;
    }
    private static GraphParticipantBindingCommandBodyV1 ReadBindingCommand(CborReader r)
    {
        Start(r,10); Need(r,1); var op=ReadOperation(r); Need(r,2); var reservation=AuthorityPositionCodecsV1.ReadJournal(r); Need(r,3); var expected=ReadPositionOption(r);
        Need(r,4); var graph=ReadGraph(r); Need(r,5); var runtime=ReadRuntime(r); Need(r,6); var plan=ReadHash(r); Need(r,7); var topology=ReadHash(r);
        Need(r,8); var executable=ReadHash(r); Need(r,9); var proof=ReadProof(r); Need(r,10); var stamp=ReadStamp(r); r.ReadEndMap();
        return new(op,reservation,expected,graph,runtime,plan,topology,executable,proof,stamp);
    }
    private static GraphParticipantBindingFactBodyV1 ReadBindingFact(CborReader r)
    {
        Start(r,14); Need(r,1); var op=ReadOperation(r); Need(r,2); var command=AuthorityPositionCodecsV1.ReadJournal(r); Need(r,3); var reservation=AuthorityPositionCodecsV1.ReadJournal(r);
        Need(r,4); var predecessor=ReadPositionOption(r); Need(r,5); var outcome=ReadOutcome(r); Need(r,6); var graph=ReadGraph(r); Need(r,7); var runtime=ReadRuntime(r);
        Need(r,8); var plan=ReadHash(r); Need(r,9); var topology=ReadHash(r); Need(r,10); var executable=ReadHash(r); Need(r,11); var binding=ReadOptional(r,ReadBinding);
        Need(r,12); var proof=ReadOptional(r,ReadProof); Need(r,13); var code=ReadSafeCode(r); Need(r,14); var stamp=ReadStamp(r); r.ReadEndMap();
        var v=new GraphParticipantBindingFactBodyV1(op,command,reservation,predecessor,outcome,graph,runtime,plan,topology,executable,binding,proof,code,stamp); ValidateBindingArms(v); return v;
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
    private static CborContentException Bad()=>new("Unexpected graph-participant field shape.");

    private static void WritePositionOption(CborWriter w,JournalPositionV1? p)=>WriteOptional(w,p,AuthorityPositionCodecsV1.Write);
    private static JournalPositionV1? ReadPositionOption(CborReader r)=>ReadOptional(r,AuthorityPositionCodecsV1.ReadJournal);
    private static void WriteOptional<T>(CborWriter w,T? value,Action<CborWriter,T> write) where T:class {w.WriteStartMap(value is null?1:2);Tag(w,1);w.WriteUInt64(value is null?0UL:1UL);if(value is not null){Tag(w,2);write(w,value);}w.WriteEndMap();}
    private static void WriteOptional(CborWriter w,JournalPositionV1? value,Action<CborWriter,JournalPositionV1> write){w.WriteStartMap(value is null?1:2);Tag(w,1);w.WriteUInt64(value is null?0UL:1UL);if(value is { } x){Tag(w,2);write(w,x);}w.WriteEndMap();}
    private static T? ReadOptional<T>(CborReader r,Func<CborReader,T> read) where T:class {var n=r.ReadStartMap();Need(r,1);var k=r.ReadUInt64();if(n==1&&k==0){r.ReadEndMap();return null;}if(n!=2||k!=1)throw Bad();Need(r,2);var v=read(r);r.ReadEndMap();return v;}
    private static JournalPositionV1? ReadOptional(CborReader r,Func<CborReader,JournalPositionV1> read){var n=r.ReadStartMap();Need(r,1);var k=r.ReadUInt64();if(n==1&&k==0){r.ReadEndMap();return null;}if(n!=2||k!=1)throw Bad();Need(r,2);var v=read(r);r.ReadEndMap();return v;}

    private static void WriteReservation(CborWriter w,GraphParticipantReservationV1 v){w.WriteStartMap(3);Tag(w,1);WriteId(w,v.ParticipantId);Tag(w,2);WriteAscii128(w,v.ParticipantFactoryKey);Tag(w,3);WriteKeys(w,v.OrderedTopologyNodeKeys);w.WriteEndMap();}
    private static GraphParticipantReservationV1 ReadReservation(CborReader r){Start(r,3);Need(r,1);var id=ReadParticipant(r);Need(r,2);var f=ReadAscii128(r);Need(r,3);var k=ReadKeys(r);r.ReadEndMap();return new(id,f,k);}
    private static void WriteBinding(CborWriter w,GraphParticipantBindingV1 v){w.WriteStartMap(3);Tag(w,1);WriteId(w,v.ParticipantId);Tag(w,2);WriteAscii128(w,v.ParticipantFactoryKey);Tag(w,3);WriteKeys(w,v.OrderedTopologyNodeKeys);w.WriteEndMap();}
    private static GraphParticipantBindingV1 ReadBinding(CborReader r){Start(r,3);Need(r,1);var id=ReadParticipant(r);Need(r,2);var f=ReadAscii128(r);Need(r,3);var k=ReadKeys(r);r.ReadEndMap();return new(id,f,k);}
    private static void WriteProof(CborWriter w,CapacityGrantBindingProofV1 v){if(v.RequiredChargeCount is <1 or >3)throw Bad();w.WriteStartMap(5);Tag(w,1);WriteId(w,v.GrantId);Tag(w,2);AuthorityPositionCodecsV1.Write(w,v.GrantedAt);Tag(w,3);AuthorityPositionCodecsV1.Write(w,v.CurrentFact);Tag(w,4);w.WriteUInt64(v.RequiredChargeCount);Tag(w,5);WriteHash(w,v.RequiredChargeCoverageHash);w.WriteEndMap();}
    private static CapacityGrantBindingProofV1 ReadProof(CborReader r){Start(r,5);Need(r,1);var id=ReadGrant(r);Need(r,2);var granted=AuthorityPositionCodecsV1.ReadJournal(r);Need(r,3);var current=AuthorityPositionCodecsV1.ReadJournal(r);Need(r,4);var count=checked((ushort)r.ReadUInt64());if(count is <1 or >3)throw Bad();Need(r,5);var hash=ReadHash(r);r.ReadEndMap();return new(id,granted,current,count,hash);}
    private static void WriteKeys(CborWriter w,IReadOnlyList<BoundedAscii> keys){if(keys is null||keys.Count is <1 or >64)throw Bad();w.WriteStartArray(keys.Count);BoundedAscii prior=default;foreach(var key in keys){if(!key.IsValid||key.ToString().Length>128||(prior.IsValid&&prior.CompareTo(key)>=0))throw Bad();BoundedAsciiCodec.Write(w,key);prior=key;}w.WriteEndArray();}
    private static IReadOnlyList<BoundedAscii> ReadKeys(CborReader r){var n=r.ReadStartArray();if(n is null or <1 or >64)throw Bad();var a=new BoundedAscii[n.Value];BoundedAscii prior=default;for(var i=0;i<a.Length;i++){a[i]=ReadAscii128(r);if(prior.IsValid&&prior.CompareTo(a[i])>=0)throw Bad();prior=a[i];}r.ReadEndArray();return Array.AsReadOnly(a);}
    private static void WriteAscii128(CborWriter w,BoundedAscii v){if(!v.IsValid||v.ToString().Length>128)throw Bad();BoundedAsciiCodec.Write(w,v);}
    private static BoundedAscii ReadAscii128(CborReader r){var v=BoundedAsciiCodec.Read(r);if(v.ToString().Length>128)throw Bad();return v;}
    private static readonly HashSet<string> SafeCodes=new(StringComparer.Ordinal){"participant-already-reserved","participant-binding-already-applied","reservation-predecessor-conflict","binding-predecessor-conflict","authority-stale","plan-fingerprint-mismatch","topology-node-set-mismatch","participant-factory-mismatch","participant-id-collision","reservation-missing","capacity-grant-missing","capacity-grant-not-reserved","capacity-grant-expired-or-incomparable","capacity-grant-mismatch","capacity-charge-mismatch","factory-participant-echo-mismatch","invalid-body"};
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
    private static void WriteId(CborWriter w,CapacityGrantId v){Span<byte>b=stackalloc byte[16];if(!v.TryWriteBytes(b))throw Bad();w.WriteByteString(b);}
    private static StableId128 ReadStable(CborReader r){Span<byte>b=stackalloc byte[16];if(!r.TryReadByteString(b,out var n)||n!=16)throw Bad();return StableId128.FromBytes(b);}
    private static OperationId ReadOperation(CborReader r)=>OperationId.FromValue(ReadStable(r));
    private static RuntimeGenerationId ReadRuntime(CborReader r)=>RuntimeGenerationId.FromValue(ReadStable(r));
    private static GraphGenerationId ReadGraph(CborReader r)=>GraphGenerationId.FromValue(ReadStable(r));
    private static ParticipantId ReadParticipant(CborReader r)=>ParticipantId.FromValue(ReadStable(r));
    private static CapacityGrantId ReadGrant(CborReader r)=>CapacityGrantId.FromValue(ReadStable(r));
    private static ushort ReadOutcome(CborReader r){var x=checked((ushort)r.ReadUInt64());ValidateOutcome(x);return x;}
    private static void ValidateOutcome(ushort x){if(x is not (1 or 2))throw Bad();}
    private static void ValidateReservationArms(GraphParticipantReservationFactBodyV1 v){if(v.Outcome==1?(v.Reservation is null||v.SafeCode is not null):(v.Reservation is not null||v.SafeCode is null))throw Bad();}
    private static void ValidateBindingArms(GraphParticipantBindingFactBodyV1 v){if(v.Outcome==1?(v.Binding is null||v.CapacityGrantProof is null||v.SafeCode is not null):(v.Binding is not null||v.CapacityGrantProof is not null||v.SafeCode is null))throw Bad();}
}
