using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Auth.Base;
using HPD.Base;

namespace HPD.Auth.Infrastructure.Base;

/// <summary>Implements the closed L2A deterministic Auth record-identity codec.</summary>
internal static class AuthBaseDeterministicId
{
    internal static string Create(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        using var stream = new MemoryStream();
        stream.Write("hpd.auth.id.v1"u8);
        WriteInt32(stream, parts.Length);
        foreach (string part in parts)
        {
            ArgumentNullException.ThrowIfNull(part);
            byte[] utf8 = Encoding.UTF8.GetBytes(part.Normalize(NormalizationForm.FormC));
            WriteInt32(stream, utf8.Length);
            stream.Write(utf8);
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    /// <summary>
    /// Creates the restore-stable L3 cleanup-work identity for one exact subject lifetime.
    /// </summary>
    internal static string CreateCleanupWork<TSubject>(
        Guid tenantId,
        string subjectKind,
        Guid subjectId,
        BaseExportedSubjectContract<TSubject> contract,
        BaseSubjectIncarnation incarnation,
        long tombstoneSequence)
    {
        return AuthCleanupWorkIdentity.Create(
            tenantId, subjectKind, subjectId,
            subjectKind switch
            {
                "user" => AuthUserSubject.HPDBaseSubjectRegistration,
                "role" => AuthRoleSubject.HPDBaseSubjectRegistration,
                _ => throw new ArgumentOutOfRangeException(nameof(subjectKind)),
            },
            incarnation, tombstoneSequence);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
