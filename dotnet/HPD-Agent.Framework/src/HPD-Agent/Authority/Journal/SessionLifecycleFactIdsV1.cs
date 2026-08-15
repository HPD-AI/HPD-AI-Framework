using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Agent.Authority;

internal static class SessionLifecycleCommandFactIdV1
{
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("hpd-session-lifecycle-command-fact-id-v1\0");

    internal static JournalFactId Derive(SessionAuthorityStampV1 session, OperationId operationId)
    {
        if (!session.IsValid) throw new ArgumentException("A session authority stamp is required.", nameof(session));
        if (!operationId.IsValid) throw new ArgumentException("A lifecycle operation identity is required.", nameof(operationId));
        Span<byte> identity = stackalloc byte[48];
        if (!session.RuntimeGenerationId.TryWriteBytes(identity) ||
            !session.LiveSessionId.TryWriteBytes(identity[16..]) ||
            !operationId.TryWriteBytes(identity[32..]))
            throw new ArgumentException("Lifecycle command identity components must be valid.");
        return SessionLifecycleFactIdDerivationV1.Derive(Domain, identity);
    }
}

internal static class SessionLifecycleResultFactIdV1
{
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("hpd-session-lifecycle-result-fact-id-v1\0");

    internal static JournalFactId Derive(JournalPositionV1 commandPosition)
    {
        if (!commandPosition.IsValid) throw new ArgumentException("An admitted lifecycle command position is required.", nameof(commandPosition));
        Span<byte> identity = stackalloc byte[40];
        if (!commandPosition.Session.RuntimeGenerationId.TryWriteBytes(identity) ||
            !commandPosition.Session.LiveSessionId.TryWriteBytes(identity[16..]))
            throw new ArgumentException("The lifecycle command session must be valid.", nameof(commandPosition));
        BinaryPrimitives.WriteInt64BigEndian(identity[32..], commandPosition.Sequence);
        return SessionLifecycleFactIdDerivationV1.Derive(Domain, identity);
    }
}

internal static class SessionLifecycleFactIdDerivationV1
{
    internal static JournalFactId Derive(ReadOnlySpan<byte> domain, ReadOnlySpan<byte> identity)
    {
        Span<byte> digest = stackalloc byte[32];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(domain);
        hash.AppendData(identity);
        if (!hash.TryGetHashAndReset(digest, out var written) || written != digest.Length)
            throw new CryptographicException("SHA-256 did not produce a complete lifecycle fact identity.");
        var candidate = digest[..16];
        if (candidate.IndexOfAnyExcept((byte)0) < 0) candidate[^1] = 1;
        return JournalFactId.FromValue(StableId128.FromBytes(candidate));
    }
}
