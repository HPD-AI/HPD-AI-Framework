using System.Security.Cryptography;

namespace HPD.Agent.Authority;

internal static class CapacityFactIdsV1
{
    private static ReadOnlySpan<byte> ReservationDomain => "hpd-capacity-reservation-fact-id-v1\0"u8;
    private static ReadOnlySpan<byte> SettlementDomain => "hpd-capacity-settlement-fact-id-v1\0"u8;

    internal static JournalFactId Reservation(CapacityGrantId grantId)
    {
        Span<byte> input = stackalloc byte[ReservationDomain.Length + 16];
        ReservationDomain.CopyTo(input);
        if (!grantId.TryWriteBytes(input[ReservationDomain.Length..]))
            throw new ArgumentException("The grant identity is invalid.", nameof(grantId));
        return Derive(input);
    }

    internal static JournalFactId Settlement(CapacityGrantId grantId, OperationId operationId)
    {
        Span<byte> input = stackalloc byte[SettlementDomain.Length + 32];
        SettlementDomain.CopyTo(input);
        if (!grantId.TryWriteBytes(input[SettlementDomain.Length..]) ||
            !operationId.TryWriteBytes(input[(SettlementDomain.Length + 16)..]))
            throw new ArgumentException("The grant and settlement operation identities must be valid.");
        return Derive(input);
    }

    private static JournalFactId Derive(ReadOnlySpan<byte> preimage)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(preimage, digest);
        Span<byte> value = digest[..16];
        if (value.IndexOfAnyExcept((byte)0) < 0) value[^1] = 1;
        return JournalFactId.FromValue(StableId128.FromBytes(value));
    }
}
