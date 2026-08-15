using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HPD.Agent.Authority;

internal static class GraphParticipantReservationFactIdsV2
{
    private static ReadOnlySpan<byte> ReservationCommandDomain => "hpd-s1-graph-participant-reservation-command-fact-id-v2\0"u8;
    private static ReadOnlySpan<byte> ReservationFactDomain => "hpd-s1-graph-participant-reservation-result-fact-id-v2\0"u8;

    internal static JournalFactId ReservationCommand(SessionAuthorityStampV1 session,OperationId operationId)
    {
        if(!session.LiveSessionId.IsValid||!operationId.IsValid)throw new ArgumentException("Valid session and operation identities are required.");
        var domain=ReservationCommandDomain;Span<byte> preimage=stackalloc byte[domain.Length+2*(1+4+16)];domain.CopyTo(preimage);var offset=domain.Length;
        preimage[offset++]=1;BinaryPrimitives.WriteUInt32BigEndian(preimage[offset..],16);offset+=4;if(!session.LiveSessionId.TryWriteBytes(preimage[offset..]))throw new ArgumentException("Invalid session.");offset+=16;
        preimage[offset++]=2;BinaryPrimitives.WriteUInt32BigEndian(preimage[offset..],16);offset+=4;if(!operationId.TryWriteBytes(preimage[offset..]))throw new ArgumentException("Invalid operation.");offset+=16;
        Span<byte> digest=stackalloc byte[32];SHA256.HashData(preimage,digest);var repaired=RepairZero(digest);return JournalFactId.FromValue(repaired);
    }

    internal static JournalFactId ReservationFact(JournalPositionV1 commandPosition)
    {
        if(!commandPosition.Session.LiveSessionId.IsValid||commandPosition.Sequence<=0)throw new ArgumentException("A valid session and positive command sequence are required.");
        var domain=ReservationFactDomain;Span<byte> preimage=stackalloc byte[domain.Length+(1+4+16)+(1+4+8)];domain.CopyTo(preimage);var offset=domain.Length;
        preimage[offset++]=1;BinaryPrimitives.WriteUInt32BigEndian(preimage[offset..],16);offset+=4;if(!commandPosition.Session.LiveSessionId.TryWriteBytes(preimage[offset..]))throw new ArgumentException("Invalid session.");offset+=16;
        preimage[offset++]=2;BinaryPrimitives.WriteUInt32BigEndian(preimage[offset..],8);offset+=4;BinaryPrimitives.WriteInt64BigEndian(preimage[offset..],commandPosition.Sequence);offset+=8;
        Span<byte> digest=stackalloc byte[32];SHA256.HashData(preimage,digest);var repaired=RepairZero(digest);return JournalFactId.FromValue(repaired);
    }

    internal static StableId128 RepairZero(ReadOnlySpan<byte> digest){if(digest.Length<16)throw new ArgumentException("A digest requires at least 16 bytes.",nameof(digest));Span<byte> id=stackalloc byte[16];digest[..16].CopyTo(id);if(id.IndexOfAnyExcept((byte)0)<0)id[^1]=1;return StableId128.FromBytes(id);}
}
