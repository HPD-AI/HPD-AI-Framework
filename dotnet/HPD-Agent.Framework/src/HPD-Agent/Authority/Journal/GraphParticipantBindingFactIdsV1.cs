using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.Authority;

internal static class GraphParticipantBindingFactIdsV1
{
    private static ReadOnlySpan<byte> ParticipantDomain => "hpd-s1-graph-participant-id-reservation-v1\0"u8;
    private static ReadOnlySpan<byte> ReservationCommandDomain => "hpd-s1-graph-participant-reservation-command-fact-id-v1\0"u8;
    private static ReadOnlySpan<byte> ReservationFactDomain => "hpd-s1-graph-participant-reservation-result-fact-id-v1\0"u8;
    private static ReadOnlySpan<byte> BindingCommandDomain => "hpd-s1-graph-participant-binding-command-fact-id-v1\0"u8;
    private static ReadOnlySpan<byte> BindingFactDomain => "hpd-s1-graph-participant-binding-result-fact-id-v1\0"u8;

    internal static ParticipantId Participant(SessionAuthorityStampV1 session, OperationId operationId) =>
        ParticipantId.FromValue(Derive(ParticipantDomain, session.LiveSessionId, operationId));

    internal static JournalFactId ReservationCommand(SessionAuthorityStampV1 session, OperationId operationId) =>
        JournalFactId.FromValue(Derive(ReservationCommandDomain, session.LiveSessionId, operationId));

    internal static JournalFactId ReservationFact(JournalPositionV1 commandPosition) =>
        JournalFactId.FromValue(Derive(ReservationFactDomain, commandPosition.Session.LiveSessionId, commandPosition.Sequence));

    internal static JournalFactId BindingCommand(SessionAuthorityStampV1 session, OperationId operationId) =>
        JournalFactId.FromValue(Derive(BindingCommandDomain, session.LiveSessionId, operationId));

    internal static JournalFactId BindingFact(JournalPositionV1 commandPosition) =>
        JournalFactId.FromValue(Derive(BindingFactDomain, commandPosition.Session.LiveSessionId, commandPosition.Sequence));

    private static StableId128 Derive(ReadOnlySpan<byte> domain, LiveSessionId session, OperationId operation)
    {
        if (!session.IsValid || !operation.IsValid) throw new ArgumentException("Valid session and operation identities are required.");
        Span<byte> preimage = stackalloc byte[domain.Length + 2 * (1 + 4 + 16)];
        domain.CopyTo(preimage); var offset=domain.Length;
        offset=Write(preimage,offset,1,session); offset=Write(preimage,offset,2,operation);
        return Hash(preimage[..offset]);
    }

    private static StableId128 Derive(ReadOnlySpan<byte> domain, LiveSessionId session, long sequence)
    {
        if (!session.IsValid || sequence<=0) throw new ArgumentException("A valid session and positive command sequence are required.");
        Span<byte> preimage = stackalloc byte[domain.Length + (1+4+16) + (1+4+8)];
        domain.CopyTo(preimage); var offset=domain.Length;
        offset=Write(preimage,offset,1,session); preimage[offset++]=2; BinaryPrimitives.WriteUInt32BigEndian(preimage[offset..],8); offset+=4;
        BinaryPrimitives.WriteInt64BigEndian(preimage[offset..],sequence); offset+=8; return Hash(preimage[..offset]);
    }

    private static int Write(Span<byte> target,int offset,byte tag,LiveSessionId value)
    { target[offset++]=tag; BinaryPrimitives.WriteUInt32BigEndian(target[offset..],16); offset+=4; if(!value.TryWriteBytes(target[offset..]))throw new ArgumentException("Invalid session."); return offset+16; }
    private static int Write(Span<byte> target,int offset,byte tag,OperationId value)
    { target[offset++]=tag; BinaryPrimitives.WriteUInt32BigEndian(target[offset..],16); offset+=4; if(!value.TryWriteBytes(target[offset..]))throw new ArgumentException("Invalid operation."); return offset+16; }
    private static StableId128 Hash(ReadOnlySpan<byte> preimage){Span<byte> digest=stackalloc byte[32];SHA256.HashData(preimage,digest);return RepairZero(digest);}
    internal static StableId128 RepairZero(ReadOnlySpan<byte> digest){if(digest.Length<16)throw new ArgumentException("A digest requires at least 16 bytes.",nameof(digest));Span<byte> id=stackalloc byte[16];digest[..16].CopyTo(id);if(id.IndexOfAnyExcept((byte)0)<0)id[^1]=1;return StableId128.FromBytes(id);}
}
