using System.Buffers;
using System.Formats.Cbor;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal static class GraphRuntimeCodecsV1
{
    internal const string CommandOuterSchemaId = "hpd.authority-payload-graph-runtime-command.v1";
    internal const string FactOuterSchemaId = "hpd.authority-payload-graph-runtime-fact.v1";
    internal const string CommandSchemaId = "hpd.graph-runtime-command.v1";
    internal const string SnapshotSchemaId = "hpd.graph-runtime-snapshot.v1";
    internal const string FactSchemaId = "hpd.graph-runtime-fact.v1";
    internal const ushort Major = 1, Minor = 0;
    internal const int MaximumBodyBytes = 65_536, MaximumOuterBytes = 65_920;

    internal static byte[] EncodeCommand(GraphRuntimeCommandV1 value)
    {
        ArgumentNullException.ThrowIfNull(value); var arm = Writer();
        arm.WriteStartMap(value is GraphRuntimeCommandV1.Activate ? 7 : 4);
        arm.WriteUInt64(1); WriteId(arm, value.OperationId.TryWriteBytes); arm.WriteUInt64(2); WritePosition(arm, value.ExpectedPredecessor);
        if (value is GraphRuntimeCommandV1.Activate a)
        { arm.WriteUInt64(3); WritePosition(arm,a.GraphAuthorityFact);arm.WriteUInt64(4);WriteHash(arm,a.TopologyFingerprint);arm.WriteUInt64(5);WriteId(arm,a.GraphGeneration.TryWriteBytes);arm.WriteUInt64(6);WritePosition(arm,a.CapacityGrantFact);arm.WriteUInt64(7);WriteHash(arm,a.EffectRequestHash); }
        else if(value is GraphRuntimeCommandV1.Retire r){arm.WriteUInt64(3);WritePosition(arm,r.ActiveRuntimeFact);arm.WriteUInt64(4);WriteHash(arm,r.EffectRequestHash);} else throw Invalid();
        arm.WriteEndMap(); var w=Writer();w.WriteStartMap(2);w.WriteUInt64(1);w.WriteUInt64((ushort)value.Kind);w.WriteUInt64(2);w.WriteByteString(arm.Encode());w.WriteEndMap();return w.Encode();
    }

    internal static bool TryDecodeCommand(ReadOnlyMemory<byte> bytes,out GraphRuntimeCommandV1? value)=>TryDecode(bytes,r=>
    {
        Map(r,2,1);var kind=(GraphRuntimeCommandKindV1)Closed(r,2);Tag(r,2);var nested=Bounded(r,MaximumBodyBytes);r.ReadEndMap();var n=Reader(nested);var count=n.ReadStartMap();if(count is null||n.ReadUInt64()!=1)throw Invalid();var operation=OperationId.FromValue(ReadId(n));Tag(n,2);var predecessor=ReadPosition(n);
        GraphRuntimeCommandV1 result;
        if(kind==GraphRuntimeCommandKindV1.Activate&&count==7){Tag(n,3);var authority=ReadPosition(n);Tag(n,4);var fingerprint=ReadHash(n);Tag(n,5);var generation=GraphGenerationId.FromValue(ReadId(n));Tag(n,6);var grant=ReadPosition(n);Tag(n,7);var hash=ReadHash(n);result=new GraphRuntimeCommandV1.Activate(operation,predecessor,authority,fingerprint,generation,grant,hash);}
        else if(kind==GraphRuntimeCommandKindV1.Retire&&count==4){Tag(n,3);var active=ReadPosition(n);Tag(n,4);result=new GraphRuntimeCommandV1.Retire(operation,predecessor,active,ReadHash(n));}else throw Invalid();
        n.ReadEndMap();if(n.BytesRemaining!=0)throw Invalid();return result;
    },EncodeCommand,out value);

    internal static byte[] EncodeSnapshot(GraphRuntimeSnapshotV1 v)
    {
        ArgumentNullException.ThrowIfNull(v);var w=Writer();w.WriteStartMap(9);w.WriteUInt64(1);w.WriteUInt64((ushort)v.Phase);w.WriteUInt64(2);WriteId(w,v.GraphGeneration.TryWriteBytes);w.WriteUInt64(3);WriteHash(w,v.TopologyFingerprint);w.WriteUInt64(4);WritePosition(w,v.CapacityGrantFact);w.WriteUInt64(5);w.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(v.CurrentAuthority));w.WriteUInt64(6);WriteId(w,v.ActivationOperationId.TryWriteBytes);w.WriteUInt64(7);WritePosition(w,v.ActivationFact);w.WriteUInt64(8);WritePosition(w,v.LastRuntimeFact);w.WriteUInt64(9);w.WriteByteString(Optional(v.Retirement,static(w,x)=>{w.WriteStartMap(2);w.WriteUInt64(1);WriteId(w,x.OperationId.TryWriteBytes);w.WriteUInt64(2);WritePosition(w,x.RetireCommandFact);w.WriteEndMap();}));w.WriteEndMap();return w.Encode();
    }

    internal static bool TryDecodeSnapshot(ReadOnlyMemory<byte>b,out GraphRuntimeSnapshotV1? value)=>TryDecode(b,r=>ReadSnapshot(r),EncodeSnapshot,out value);
    private static GraphRuntimeSnapshotV1 ReadSnapshot(CborReader r)
    {
        Map(r,9,1);var phase=(GraphRuntimePhaseV1)Closed(r,2);Tag(r,2);var generation=GraphGenerationId.FromValue(ReadId(r));Tag(r,3);var fingerprint=ReadHash(r);Tag(r,4);var grant=ReadPosition(r);Tag(r,5);if(!AuthorityVectorCodecsV1.TryDecodeVector(r.ReadEncodedValue(),out var authority))throw Invalid();Tag(r,6);var operation=OperationId.FromValue(ReadId(r));Tag(r,7);var activation=ReadPosition(r);Tag(r,8);var last=ReadPosition(r);Tag(r,9);var retirement=ReadOptional(Bounded(r,4096));r.ReadEndMap();return new GraphRuntimeSnapshotV1(phase,generation,fingerprint,grant,authority!,operation,activation,last,retirement);
    }

    internal static byte[] EncodeFact(GraphRuntimeFactV1 v)
    {
        ArgumentNullException.ThrowIfNull(v);var w=Writer();w.WriteStartMap(7);w.WriteUInt64(1);WritePosition(w,v.CommandFact);w.WriteUInt64(2);WritePosition(w,v.ExpectedPredecessor);w.WriteUInt64(3);WritePosition(w,v.ActualPredecessor);w.WriteUInt64(4);w.WriteUInt64((ushort)v.Outcome);w.WriteUInt64(5);w.WriteByteString(Optional(v.ResultingSnapshot,static(w,x)=>w.WriteEncodedValue(EncodeSnapshot(x))));w.WriteUInt64(6);w.WriteByteString(OptionalHash(v.EffectReceiptHash));w.WriteUInt64(7);w.WriteByteString(OptionalCode(v.SafeCode));w.WriteEndMap();return w.Encode();
    }
    internal static bool TryDecodeFact(ReadOnlyMemory<byte>b,out GraphRuntimeFactV1? value)=>TryDecode(b,r=>
    {Map(r,7,1);var command=ReadPosition(r);Tag(r,2);var expected=ReadPosition(r);Tag(r,3);var actual=ReadPosition(r);Tag(r,4);var outcome=(GraphRuntimeOutcomeV1)Closed(r,5);Tag(r,5);var snapshot=ReadOptionalSnapshot(Bounded(r,MaximumBodyBytes));Tag(r,6);var receipt=ReadOptionalHash(Bounded(r,4096));Tag(r,7);var code=ReadOptionalCode(Bounded(r,4096));r.ReadEndMap();return new GraphRuntimeFactV1(command,expected,actual,outcome,snapshot,receipt,code);},EncodeFact,out value);

    internal static byte[] EncodeOuter(GraphRuntimeOwnerPayloadV1 v){ArgumentNullException.ThrowIfNull(v);var w=Writer();w.WriteStartMap(3);w.WriteUInt64(1);w.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(v.Session));w.WriteUInt64(2);w.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(v.ExpectedAuthority));w.WriteUInt64(3);w.WriteByteString(v.Body.Span);w.WriteEndMap();return w.Encode();}
    internal static bool TryDecodeOuter(ReadOnlyMemory<byte>b,out GraphRuntimeOwnerPayloadV1? value)=>TryDecode(b,r=>{Map(r,3,1);if(!SessionAuthorityStampV1Codec.TryDecode(r.ReadEncodedValue(),out var s))throw Invalid();Tag(r,2);if(!AuthorityVectorCodecsV1.TryDecodeVector(r.ReadEncodedValue(),out var a))throw Invalid();Tag(r,3);var body=Bounded(r,MaximumBodyBytes);r.ReadEndMap();return new GraphRuntimeOwnerPayloadV1(s,a!,body.Span);},EncodeOuter,out value);
    internal static Hash256 Hash(string schema,ReadOnlySpan<byte> canonical)=>AuthorityIntegrityHashV1.Compute(schema,Major,Minor,canonical);
    internal static Hash256 ComputeHash(GraphRuntimeCommandV1 value)=>Hash(CommandSchemaId,EncodeCommand(value));
    internal static Hash256 ComputeHash(GraphRuntimeSnapshotV1 value)=>Hash(SnapshotSchemaId,EncodeSnapshot(value));
    internal static Hash256 ComputeHash(GraphRuntimeFactV1 value)=>Hash(FactSchemaId,EncodeFact(value));

    private static byte[] Optional<T>(T? v,Action<CborWriter,T> write)where T:class{var w=Writer();w.WriteStartMap(2);w.WriteUInt64(1);w.WriteUInt64(v is null?0UL:1UL);w.WriteUInt64(2);if(v is null)w.WriteByteString([]);else{var n=Writer();write(n,v);w.WriteByteString(n.Encode());}w.WriteEndMap();return w.Encode();}
    private static GraphRuntimeRetirementV1? ReadOptional(ReadOnlyMemory<byte>b)=>ReadOptionalCore(b,r=>{Map(r,2,1);var o=OperationId.FromValue(ReadId(r));Tag(r,2);var p=ReadPosition(r);r.ReadEndMap();return new GraphRuntimeRetirementV1(o,p);});
    private static GraphRuntimeSnapshotV1? ReadOptionalSnapshot(ReadOnlyMemory<byte>b)=>ReadOptionalCore(b,r=>ReadSnapshot(r));
    private static T? ReadOptionalCore<T>(ReadOnlyMemory<byte>b,Func<CborReader,T> read)where T:class{var r=Reader(b);Map(r,2,1);var k=r.ReadUInt64();Tag(r,2);T? v;if(k==0){if(r.ReadByteString().Length!=0)throw Invalid();v=null;}else if(k==1){var n=Reader(Bounded(r,MaximumBodyBytes));v=read(n);if(n.BytesRemaining!=0)throw Invalid();}else throw Invalid();r.ReadEndMap();if(r.BytesRemaining!=0)throw Invalid();return v;}
    private static byte[] OptionalHash(Hash256? h)=>OptionalBox(h is null?null:new HashBox(h.Value),static(w,x)=>WriteHash(w,x.Value));
    private static Hash256? ReadOptionalHash(ReadOnlyMemory<byte>b)=>ReadOptionalCore(b,r=>new HashBox(ReadHash(r)))?.Value;
    private static byte[] OptionalCode(BoundedAscii? c)=>OptionalBox(c is null?null:new CodeBox(c.Value),static(w,x)=>BoundedAsciiCodec.Write(w,x.Value));
    private static BoundedAscii? ReadOptionalCode(ReadOnlyMemory<byte>b)=>ReadOptionalCore(b,r=>new CodeBox(BoundedAsciiCodec.Read(r)))?.Value;
    private static byte[] OptionalBox<T>(T? v,Action<CborWriter,T> write)where T:class=>Optional(v,write);
    private sealed record HashBox(Hash256 Value);private sealed record CodeBox(BoundedAscii Value);
    private delegate bool IdWrite(Span<byte>b);private static void WriteId(CborWriter w,IdWrite f){Span<byte>b=stackalloc byte[16];if(!f(b))throw Invalid();w.WriteByteString(b);}private static StableId128 ReadId(CborReader r){Span<byte>b=stackalloc byte[16];if(!r.TryReadByteString(b,out var n)||n!=16||b.IndexOfAnyExcept((byte)0)<0)throw Invalid();return StableId128.FromBytes(b);}
    private static void WriteHash(CborWriter w,Hash256 h){Span<byte>b=stackalloc byte[32];if(!h.TryWriteBytes(b))throw Invalid();w.WriteByteString(b);}private static Hash256 ReadHash(CborReader r){Span<byte>b=stackalloc byte[32];if(!r.TryReadByteString(b,out var n)||n!=32)throw Invalid();return Hash256.FromBytes(b);}
    private static void WritePosition(CborWriter w,JournalPositionV1 p)=>w.WriteEncodedValue(AuthorityPositionCodecsV1.Encode(p));private static JournalPositionV1 ReadPosition(CborReader r){if(!AuthorityPositionCodecsV1.TryDecodeJournal(r.ReadEncodedValue(),out var p))throw Invalid();return p;}
    private static ReadOnlyMemory<byte> Bounded(CborReader r,int max){var a=ArrayPool<byte>.Shared.Rent(max);try{if(!r.TryReadByteString(a.AsSpan(0,max),out var n)||n is 0||n>max)throw Invalid();return a.AsMemory(0,n).ToArray();}finally{ArrayPool<byte>.Shared.Return(a);}}
    private static CborWriter Writer()=>new(CborConformanceMode.Ctap2Canonical);private static CborReader Reader(ReadOnlyMemory<byte>b)=>new(b,CborConformanceMode.Ctap2Canonical,false);private static void Map(CborReader r,int n,ulong tag){if(r.ReadStartMap()!=n||r.ReadUInt64()!=tag)throw Invalid();}private static void Tag(CborReader r,ulong t){if(r.ReadUInt64()!=t)throw Invalid();}private static ushort Closed(CborReader r,ushort max){var x=r.ReadUInt64();if(x is 0||x>max)throw Invalid();return(ushort)x;}private static CborContentException Invalid()=>new("Invalid canonical graph-runtime payload.");
    private static bool TryDecode<T>(ReadOnlyMemory<byte>b,Func<CborReader,T> read,Func<T,byte[]> encode,out T? value)where T:class{value=null;if(b.Length is 0 or>MaximumOuterBytes)return false;try{var r=Reader(b);var v=read(r);if(r.BytesRemaining!=0||!encode(v).AsSpan().SequenceEqual(b.Span))return false;value=v;return true;}catch(Exception e)when(e is CborContentException or InvalidOperationException or ArgumentException or OverflowException){return false;}}
}
