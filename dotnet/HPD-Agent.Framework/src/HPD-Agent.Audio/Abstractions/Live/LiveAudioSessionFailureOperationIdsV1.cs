using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio;

internal readonly record struct LiveAudioSessionFailureOperationIdsV1(
    OperationId Begin, OperationId Advance, OperationId Complete)
{
    private static readonly byte[] BeginDomain = Encoding.ASCII.GetBytes("hpd-live-audio-start-failure-begin-operation-id-v1\0");
    private static readonly byte[] AdvanceDomain = Encoding.ASCII.GetBytes("hpd-live-audio-start-failure-advance-operation-id-v1\0");
    private static readonly byte[] CompleteDomain = Encoding.ASCII.GetBytes("hpd-live-audio-start-failure-complete-operation-id-v1\0");

    internal static LiveAudioSessionFailureOperationIdsV1 Derive(
        LiveAudioSessionStartRequestV1 request, JournalPositionV1 reservationPosition)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!reservationPosition.IsValid || reservationPosition.Session != request.ExpectedAuthority.Session)
            throw new ArgumentException("The reservation position must belong to the request session.", nameof(reservationPosition));
        Span<byte> identity = stackalloc byte[88];
        if (!reservationPosition.Session.RuntimeGenerationId.TryWriteBytes(identity) ||
            !reservationPosition.Session.LiveSessionId.TryWriteBytes(identity[16..]) ||
            !request.OperationId.TryWriteBytes(identity[40..]))
            throw new ArgumentException("The failure-operation identity components must be valid.");
        BinaryPrimitives.WriteInt64BigEndian(identity[32..], reservationPosition.Sequence);
        if (!request.Fingerprint.TryWriteBytes(identity[56..]))
            throw new ArgumentException("The request fingerprint is required.", nameof(request));
        return new(Derive(BeginDomain, identity), Derive(AdvanceDomain, identity), Derive(CompleteDomain, identity));
    }

    private static OperationId Derive(ReadOnlySpan<byte> domain, ReadOnlySpan<byte> identity)
    {
        Span<byte> digest = stackalloc byte[32];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(domain); hash.AppendData(identity);
        if (!hash.TryGetHashAndReset(digest, out var written) || written != digest.Length)
            throw new CryptographicException("SHA-256 did not produce a complete failure-operation identity.");
        var candidate = digest[..16];
        if (candidate.IndexOfAnyExcept((byte)0) < 0) candidate[^1] = 1;
        return OperationId.FromValue(StableId128.FromBytes(candidate));
    }
}
