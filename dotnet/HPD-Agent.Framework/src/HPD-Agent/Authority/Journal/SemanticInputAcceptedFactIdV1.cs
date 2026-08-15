using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Authority;

internal static class SemanticInputAcceptedFactIdV1
{
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("hpd-s4-l3-fact-id-v1\0");

    internal static JournalFactId Derive(Hash256 payloadHash)
    {
        Span<byte> payloadHashBytes = stackalloc byte[32];
        if (!payloadHash.TryWriteBytes(payloadHashBytes))
            throw new ArgumentException("A schema-bound semantic payload hash is required.", nameof(payloadHash));
        Span<byte> preimage = stackalloc byte[Domain.Length + 32];
        Domain.CopyTo(preimage);
        payloadHashBytes.CopyTo(preimage[Domain.Length..]);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(preimage, digest);
        var candidate = digest[..16];
        if (candidate.IndexOfAnyExcept((byte)0) < 0) candidate[^1] = 1;
        return JournalFactId.FromValue(StableId128.FromBytes(candidate));
    }
}
