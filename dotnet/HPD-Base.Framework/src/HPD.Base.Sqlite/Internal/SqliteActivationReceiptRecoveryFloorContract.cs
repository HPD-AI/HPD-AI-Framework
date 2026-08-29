using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base.Sqlite;

/// <summary>Owns canonical authority for one semantic-recovery activation-receipt floor.</summary>
internal static class SqliteActivationReceiptRecoveryFloorContract
{
    /// <summary>Computes the canonical floor checksum.</summary>
    public static byte[] Checksum(
        string activationId,
        string definitionId,
        int definitionVersion,
        ReadOnlySpan<byte> definitionChecksum,
        int scopeKind,
        ReadOnlySpan<byte> scopeDigest,
        string semanticDefinitionId,
        ReadOnlySpan<byte> semanticBindingId,
        ReadOnlySpan<byte> semanticKeyDigest,
        ReadOnlySpan<byte> semanticAuthorityChecksum)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.receiptRecoveryFloor.v1\0"u8);
        void Append(ReadOnlySpan<byte> value)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
            hash.AppendData(length);
            hash.AppendData(value);
        }
        Span<byte> integer = stackalloc byte[8];
        Append(Encoding.UTF8.GetBytes(activationId));
        Append(Encoding.UTF8.GetBytes(definitionId));
        BinaryPrimitives.WriteInt64BigEndian(integer, definitionVersion); hash.AppendData(integer);
        Append(definitionChecksum);
        BinaryPrimitives.WriteInt64BigEndian(integer, scopeKind); hash.AppendData(integer);
        Append(scopeDigest);
        Append(Encoding.UTF8.GetBytes(semanticDefinitionId));
        Append(semanticBindingId);
        Append(semanticKeyDigest);
        Append(semanticAuthorityChecksum);
        return hash.GetHashAndReset();
    }
}
