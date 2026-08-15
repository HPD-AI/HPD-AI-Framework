using System.Buffers.Binary;
using System.Security.Cryptography;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Graph;

internal static class GraphRuntimeFactIdsV1
{
    internal static JournalFactId Command(SessionAuthorityStampV1 session, OperationId operation, GraphRuntimeCommandKindV1 kind)
    { Span<byte> value = stackalloc byte[50]; WriteSession(session, value); if (!operation.TryWriteBytes(value[32..]) || !Enum.IsDefined(kind)) throw new ArgumentException("A valid command identity is required."); BinaryPrimitives.WriteUInt16BigEndian(value[48..], (ushort)kind); return Derive("hpd-graph-runtime-command-fact-id-v1\0"u8, value); }
    internal static JournalFactId Result(JournalPositionV1 command)
    { if (!command.IsValid) throw new ArgumentException("An admitted command is required.", nameof(command)); Span<byte> value = stackalloc byte[40]; WriteSession(command.Session, value); BinaryPrimitives.WriteInt64BigEndian(value[32..], command.Sequence); return Derive("hpd-graph-runtime-result-fact-id-v1\0"u8, value); }
    private static void WriteSession(SessionAuthorityStampV1 session, Span<byte> value)
    { if (!session.IsValid || !session.RuntimeGenerationId.TryWriteBytes(value) || !session.LiveSessionId.TryWriteBytes(value[16..])) throw new ArgumentException("A valid session is required."); }
    private static JournalFactId Derive(ReadOnlySpan<byte> domain, ReadOnlySpan<byte> value)
    { using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(domain); hash.AppendData(value); Span<byte> digest = stackalloc byte[32]; if (!hash.TryGetHashAndReset(digest, out var n) || n != 32) throw new CryptographicException(); var id = digest[..16]; if (id.IndexOfAnyExcept((byte)0) < 0) id[^1] = 1; return JournalFactId.FromValue(StableId128.FromBytes(id)); }
}

internal static class GraphRuntimeEffectHashesV1
{
    private static ReadOnlySpan<byte> RequestDomain => "hpd-s2-graph-runtime-effect-request-v1\0"u8;
    private static ReadOnlySpan<byte> ReceiptDomain => "hpd-s2-graph-runtime-effect-receipt-v1\0"u8;

    internal static Hash256 Activate(SessionAuthorityStampV1 session, OperationId operation, JournalPositionV1 authorityFact,
        Hash256 fingerprint, GraphGenerationId generation, JournalPositionV1 grantFact) =>
        Hash(RequestPreimage(session, GraphRuntimeCommandKindV1.Activate, operation, authorityFact, fingerprint, generation, grantFact));
    internal static Hash256 Retire(SessionAuthorityStampV1 session, OperationId operation, JournalPositionV1 activeFact) =>
        Hash(RequestPreimage(session, GraphRuntimeCommandKindV1.Retire, operation, activeFact));
    internal static Hash256 Receipt(SessionAuthorityStampV1 session, GraphRuntimeCommandKindV1 kind, OperationId operation,
        Hash256 requestHash, ReadOnlySpan<byte> receipt) => Hash(ReceiptPreimage(session, kind, operation, requestHash, receipt));

    internal static byte[] RequestPreimage(SessionAuthorityStampV1 session, GraphRuntimeCommandKindV1 kind, OperationId operation,
        JournalPositionV1 fact, Hash256 fingerprint = default, GraphGenerationId generation = default, JournalPositionV1 grant = default)
    {
        var arm = kind switch { GraphRuntimeCommandKindV1.Activate => 56, GraphRuntimeCommandKindV1.Retire => 0, _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        var bytes = new byte[RequestDomain.Length + 50 + 8 + arm]; RequestDomain.CopyTo(bytes); var offset = RequestDomain.Length;
        WriteIdentity(session, kind, operation, bytes.AsSpan(offset)); offset += 50;
        if (!fact.IsValid || fact.Session != session) throw new ArgumentException("The referenced fact must belong to the session.");
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(offset), fact.Sequence); offset += 8;
        if (kind == GraphRuntimeCommandKindV1.Activate)
        { if (fingerprint == default || !generation.IsValid || !grant.IsValid || grant.Session != session || !fingerprint.TryWriteBytes(bytes.AsSpan(offset))) throw new ArgumentException("Activate proof is incomplete."); offset += 32; generation.TryWriteBytes(bytes.AsSpan(offset)); offset += 16; BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(offset), grant.Sequence); }
        return bytes;
    }

    internal static byte[] ReceiptPreimage(SessionAuthorityStampV1 session, GraphRuntimeCommandKindV1 kind, OperationId operation,
        Hash256 requestHash, ReadOnlySpan<byte> receipt)
    {
        if (receipt.Length is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(receipt));
        var bytes = new byte[ReceiptDomain.Length + 50 + 32 + 4 + receipt.Length]; ReceiptDomain.CopyTo(bytes); var offset = ReceiptDomain.Length;
        WriteIdentity(session, kind, operation, bytes.AsSpan(offset)); offset += 50;
        if (!requestHash.TryWriteBytes(bytes.AsSpan(offset))) throw new ArgumentException("A request hash is required."); offset += 32;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), (uint)receipt.Length); receipt.CopyTo(bytes.AsSpan(offset + 4)); return bytes;
    }
    private static void WriteIdentity(SessionAuthorityStampV1 session, GraphRuntimeCommandKindV1 kind, OperationId operation, Span<byte> value)
    { if (!session.IsValid || !Enum.IsDefined(kind) || !session.RuntimeGenerationId.TryWriteBytes(value) || !session.LiveSessionId.TryWriteBytes(value[16..]) || !operation.TryWriteBytes(value[34..])) throw new ArgumentException("A valid effect identity is required."); BinaryPrimitives.WriteUInt16BigEndian(value[32..], (ushort)kind); }
    private static Hash256 Hash(ReadOnlySpan<byte> value) => Hash256.FromBytes(SHA256.HashData(value));
}
