using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Authority;

internal static class SemanticHandoffFactIdsV1
{
    internal static JournalFactId Reservation(Hash256 payloadHash) => Derive("hpd-s4-l1-fact-id-v1\0", payloadHash);
    internal static JournalFactId Disposition(Hash256 payloadHash) => Derive("hpd-s4-l2-fact-id-v1\0", payloadHash);
    internal static JournalFactId AcceptanceBinding(Hash256 payloadHash) => Derive("hpd-s4-l4-fact-id-v1\0", payloadHash);

    private static JournalFactId Derive(string domain, Hash256 payloadHash)
    {
        var domainBytes = Encoding.ASCII.GetBytes(domain);
        Span<byte> hashBytes = stackalloc byte[32];
        if (!payloadHash.TryWriteBytes(hashBytes)) throw new ArgumentException("A payload hash is required.", nameof(payloadHash));
        Span<byte> preimage = stackalloc byte[domainBytes.Length + 32];
        domainBytes.CopyTo(preimage);
        hashBytes.CopyTo(preimage[domainBytes.Length..]);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(preimage, digest);
        if (digest[..16].IndexOfAnyExcept((byte)0) < 0) digest[15] = 1;
        return JournalFactId.FromValue(StableId128.FromBytes(digest[..16]));
    }
}
