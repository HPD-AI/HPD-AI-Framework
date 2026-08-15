using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.Authority;

internal static class GlobalParticipantAllocatorFactIdsV1
{
    private static ReadOnlySpan<byte> SourceDomain => "hpd-s1-participant-allocation-source-v1\0"u8;
    private static ReadOnlySpan<byte> ParticipantDomain => "hpd-s1-global-participant-id-v1\0"u8;
    private static ReadOnlySpan<byte> FactDomain => "hpd-s1-global-participant-claim-record-fact-id-v1\0"u8;
    private static ReadOnlySpan<byte> RecordDomain => "hpd-s1-global-participant-claim-record-hash-v1\0"u8;

    internal static Hash256 SourceFingerprint(GlobalParticipantAllocationSourceV1 source)
    {
        var position=EncodePosition(source.SourceFactPosition);var bytes=new byte[SourceDomain.Length+1+4+16+1+4+position.Length+2*(1+4+32)];var o=0;
        SourceDomain.CopyTo(bytes);o+=SourceDomain.Length;o=Id(bytes,o,1,source.LiveSessionId);o=Blob(bytes,o,2,position);o=Hash(bytes,o,3,source.SourceOuterIntegrityHash);o=Hash(bytes,o,4,source.SourceBodyHash);return Hash256.FromBytes(SHA256.HashData(bytes.AsSpan(0,o)));
    }

    internal static ParticipantId Participant(LiveSessionId session,OperationId operation,Hash256 sourceFingerprint)=>ParticipantId.FromValue(Derive(ParticipantDomain,session,operation,sourceFingerprint));
    internal static JournalFactId Fact(LiveSessionId session,OperationId operation,Hash256 sourceFingerprint){Span<byte>s=stackalloc byte[16];Span<byte>o=stackalloc byte[16];Span<byte>f=stackalloc byte[32];if(!session.TryWriteBytes(s)||!operation.TryWriteBytes(o)||!sourceFingerprint.TryWriteBytes(f))throw new ArgumentException("Valid fact identity inputs are required.");var p=new byte[FactDomain.Length+64];FactDomain.CopyTo(p);s.CopyTo(p.AsSpan(FactDomain.Length));o.CopyTo(p.AsSpan(FactDomain.Length+16));f.CopyTo(p.AsSpan(FactDomain.Length+32));return JournalFactId.FromValue(GraphParticipantBindingFactIdsV1.RepairZero(SHA256.HashData(p)));}

    internal static Hash256 RecordHash(GlobalParticipantAuthorityPositionV1 assignedPosition,GlobalParticipantAuthorityHeadV1? priorHead,JournalFactId factId,SessionAuthorityStampV1 sourceSession,ExpectedAuthorityVectorV1 sourceExpectedAuthority,ReadOnlySpan<byte> canonicalBody)
    {
        var position=GlobalParticipantAllocatorCodecsV1.Encode(assignedPosition);var prior=EncodeOptionalHead(priorHead);var session=EncodeSession(sourceSession);var authority=EncodeAuthority(sourceExpectedAuthority);
        var bytes=new byte[RecordDomain.Length+4+position.Length+4+prior.Length+16+2+4+session.Length+4+authority.Length+4+canonicalBody.Length];var o=0;RecordDomain.CopyTo(bytes);o+=RecordDomain.Length;o=RawBlob(bytes,o,position);o=RawBlob(bytes,o,prior);
        Span<byte> id=stackalloc byte[16];if(!factId.TryWriteBytes(id))throw new ArgumentException("A valid fact ID is required.");id.CopyTo(bytes.AsSpan(o));o+=16;BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(o),42);o+=2;o=RawBlob(bytes,o,session);o=RawBlob(bytes,o,authority);o=RawBlob(bytes,o,canonicalBody);return Hash256.FromBytes(SHA256.HashData(bytes.AsSpan(0,o)));
    }

    private static StableId128 Derive(ReadOnlySpan<byte> domain,LiveSessionId session,OperationId operation,Hash256 fingerprint){var bytes=new byte[domain.Length+2*(1+4+16)+(1+4+32)];domain.CopyTo(bytes);var o=domain.Length;o=Id(bytes,o,1,session);o=Id(bytes,o,2,operation);o=Hash(bytes,o,3,fingerprint);var digest=SHA256.HashData(bytes.AsSpan(0,o));return GraphParticipantBindingFactIdsV1.RepairZero(digest);}
    private static byte[] EncodePosition(JournalPositionV1 value){var w=new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Ctap2Canonical);AuthorityPositionCodecsV1.Write(w,value);return w.Encode();}
    private static byte[] EncodeSession(SessionAuthorityStampV1 value){var w=new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Ctap2Canonical);SessionAuthorityStampV1Codec.Write(w,value);return w.Encode();}
    private static byte[] EncodeAuthority(ExpectedAuthorityVectorV1 value){var w=new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Ctap2Canonical);AuthorityVectorCodecsV1.WriteVector(w,value);return w.Encode();}
    private static byte[] EncodeOptionalHead(GlobalParticipantAuthorityHeadV1? value){var w=new System.Formats.Cbor.CborWriter(System.Formats.Cbor.CborConformanceMode.Ctap2Canonical);w.WriteStartMap(value is null?1:2);w.WriteUInt64(1);w.WriteUInt64(value is null?0UL:1UL);if(value is{}x){w.WriteUInt64(2);GlobalParticipantAllocatorCodecsV1.Head(w,x);}w.WriteEndMap();return w.Encode();}
    private static int Id(byte[] b,int o,byte tag,LiveSessionId v){Span<byte>x=stackalloc byte[16];if(!v.TryWriteBytes(x))throw new ArgumentException("Invalid session.");return Tagged(b,o,tag,x);}private static int Id(byte[] b,int o,byte tag,OperationId v){Span<byte>x=stackalloc byte[16];if(!v.TryWriteBytes(x))throw new ArgumentException("Invalid operation.");return Tagged(b,o,tag,x);}private static int Hash(byte[] b,int o,byte tag,Hash256 v){Span<byte>x=stackalloc byte[32];if(!v.TryWriteBytes(x))throw new ArgumentException("Invalid hash.");return Tagged(b,o,tag,x);}private static int Tagged(byte[] b,int o,byte tag,ReadOnlySpan<byte>x){b[o++]=tag;BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o),checked((uint)x.Length));o+=4;x.CopyTo(b.AsSpan(o));return o+x.Length;}private static int Blob(byte[]b,int o,byte tag,ReadOnlySpan<byte>x)=>Tagged(b,o,tag,x);private static int RawBlob(byte[]b,int o,ReadOnlySpan<byte>x){BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o),checked((uint)x.Length));o+=4;x.CopyTo(b.AsSpan(o));return o+x.Length;}
}
