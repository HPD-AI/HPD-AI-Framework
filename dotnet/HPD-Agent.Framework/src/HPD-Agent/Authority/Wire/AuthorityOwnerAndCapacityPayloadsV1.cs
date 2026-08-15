using System.Buffers;
using System.Formats.Cbor;

namespace HPD.Agent.Authority;

internal sealed class AuthorityOwnerPayloadV1
{
    private readonly byte[] _payload;
    internal AuthorityOwnerPayloadV1(ushort discriminator, ReadOnlySpan<byte> payload)
    {
        if (discriminator == 0 || payload.Length > 65_536) throw new ArgumentException("Invalid authority owner payload.");
        Discriminator = discriminator; _payload = payload.ToArray(); Payload = Array.AsReadOnly(_payload);
    }
    internal ushort Discriminator { get; }
    internal IReadOnlyList<byte> Payload { get; }
    internal ReadOnlySpan<byte> PayloadBytes => _payload;
}

internal abstract class CapacityAuthorityOuterV1
{
    private readonly byte[] _body;
    protected CapacityAuthorityOuterV1(SessionAuthorityStampV1 session, ExpectedAuthorityVectorV1 expectedAuthority, ReadOnlySpan<byte> body)
    {
        if (!session.IsValid || expectedAuthority is null || expectedAuthority.Session != session || body.Length > 65_536) throw new ArgumentException("Invalid capacity authority payload.");
        Session = session; ExpectedAuthority = expectedAuthority; _body = body.ToArray(); Body = Array.AsReadOnly(_body);
    }
    internal SessionAuthorityStampV1 Session { get; }
    internal ExpectedAuthorityVectorV1 ExpectedAuthority { get; }
    internal IReadOnlyList<byte> Body { get; }
    internal ReadOnlySpan<byte> BodyBytes => _body;
}
internal sealed class CapacityReservationCommandV1(SessionAuthorityStampV1 s, ExpectedAuthorityVectorV1 a, ReadOnlySpan<byte> b) : CapacityAuthorityOuterV1(s,a,b);
internal sealed class CapacitySettlementFactV1(SessionAuthorityStampV1 s, ExpectedAuthorityVectorV1 a, ReadOnlySpan<byte> b) : CapacityAuthorityOuterV1(s,a,b);

internal static class AuthorityOwnerAndCapacityPayloadCodecsV1
{
    private const int MaximumEncodedBytes = 66_560;
    internal static byte[] Encode(AuthorityOwnerPayloadV1 value)
    { ArgumentNullException.ThrowIfNull(value); var w=Writer();w.WriteStartMap(2);w.WriteUInt64(1);w.WriteUInt64(value.Discriminator);w.WriteUInt64(2);w.WriteByteString(value.PayloadBytes);w.WriteEndMap();return Bound(w.Encode()); }
    internal static byte[] Encode(CapacityAuthorityOuterV1 value)
    { ArgumentNullException.ThrowIfNull(value);var w=Writer();w.WriteStartMap(3);w.WriteUInt64(1);w.WriteEncodedValue(SessionAuthorityStampV1Codec.Encode(value.Session));w.WriteUInt64(2);w.WriteEncodedValue(AuthorityVectorCodecsV1.Encode(value.ExpectedAuthority));w.WriteUInt64(3);w.WriteByteString(value.BodyBytes);w.WriteEndMap();return Bound(w.Encode()); }
    internal static bool TryDecodeOwner(ReadOnlyMemory<byte> bytes,out AuthorityOwnerPayloadV1? value)
    {value=null;return Decode(bytes,r=>{RequireMap(r,2);RequireTag(r,1);var d=checked((ushort)r.ReadUInt64());RequireTag(r,2);var b=ReadBytes(r);r.ReadEndMap();return new AuthorityOwnerPayloadV1(d,b);},Encode,out value);}
    internal static bool TryDecodeReservation(ReadOnlyMemory<byte>b,out CapacityReservationCommandV1? v)=>DecodeOuter(b,static(s,a,x)=>new(s,a,x),out v);
    internal static bool TryDecodeSettlement(ReadOnlyMemory<byte>b,out CapacitySettlementFactV1? v)=>DecodeOuter(b,static(s,a,x)=>new(s,a,x),out v);
    internal static Hash256 ComputeHash(AuthorityOwnerPayloadV1 v)=>AuthorityIntegrityHashV1.Compute("hpd.authority-owner-payload.v1",1,0,Encode(v));
    internal static Hash256 ComputeHash(CapacityReservationCommandV1 v)=>AuthorityIntegrityHashV1.Compute("hpd.authority-payload-capacity-reservation-command.v1",1,0,Encode(v));
    internal static Hash256 ComputeHash(CapacitySettlementFactV1 v)=>AuthorityIntegrityHashV1.Compute("hpd.authority-payload-capacity-settlement-fact.v1",1,0,Encode(v));
    private delegate T OuterFactory<out T>(SessionAuthorityStampV1 s,ExpectedAuthorityVectorV1 a,ReadOnlySpan<byte>b) where T:CapacityAuthorityOuterV1;
    private static bool DecodeOuter<T>(ReadOnlyMemory<byte>b,OuterFactory<T> f,out T? v)where T:CapacityAuthorityOuterV1
    {v=null;return Decode(b,r=>{RequireMap(r,3);RequireTag(r,1);if(!SessionAuthorityStampV1Codec.TryDecode(r.ReadEncodedValue(),out var s))throw new CborContentException("Invalid session.");RequireTag(r,2);if(!AuthorityVectorCodecsV1.TryDecodeVector(r.ReadEncodedValue(),out var a))throw new CborContentException("Invalid authority.");RequireTag(r,3);var body=ReadBytes(r);r.ReadEndMap();return f(s,a!,body);},Encode,out v);}
    private static bool Decode<T>(ReadOnlyMemory<byte>b,Func<CborReader,T> read,Func<T,byte[]> encode,out T? value)where T:class
    {value=null;if(b.Length is 0 or >MaximumEncodedBytes)return false;try{var r=new CborReader(b,CborConformanceMode.Ctap2Canonical,false);var x=read(r);if(r.BytesRemaining!=0||!encode(x).AsSpan().SequenceEqual(b.Span))return false;value=x;return true;}catch(Exception e)when(e is CborContentException or InvalidOperationException or ArgumentException or OverflowException){return false;}}
    private static byte[] ReadBytes(CborReader r){var rented=ArrayPool<byte>.Shared.Rent(65_536);try{if(!r.TryReadByteString(rented,out var n)||n>65_536)throw new CborContentException("Invalid bounded bytes.");return rented.AsSpan(0,n).ToArray();}finally{ArrayPool<byte>.Shared.Return(rented,true);}}
    private static CborWriter Writer()=>new(CborConformanceMode.Ctap2Canonical);
    private static void RequireMap(CborReader r,int n){if(r.ReadStartMap()!=n)throw new CborContentException("Invalid map.");}
    private static void RequireTag(CborReader r,ulong t){if(r.ReadUInt64()!=t)throw new CborContentException("Invalid tag.");}
    private static byte[] Bound(byte[] b)=>b.Length<=MaximumEncodedBytes?b:throw new ArgumentOutOfRangeException(nameof(b));
}
