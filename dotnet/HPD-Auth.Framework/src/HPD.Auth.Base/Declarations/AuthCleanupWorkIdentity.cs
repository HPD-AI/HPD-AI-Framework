using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HPD.Base;

namespace HPD.Auth.Base;

/// <summary>Derives the restore-stable cleanup-work identity for one exact Auth subject lifetime.</summary>
internal static class AuthCleanupWorkIdentity
{
    internal static BaseMutationRequestFingerprint Fingerprint(string domain, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(values);
        using var stream = new MemoryStream();
        Write(stream, domain);
        foreach (string value in values)
            Write(stream, value);
        return BaseMutationRequestFingerprint.Create(
            SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static void Write(Stream stream, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    internal static string Create(
        Guid tenantId,
        string subjectKind,
        Guid subjectId,
        BaseGeneratedSubjectRegistration contract,
        BaseSubjectIncarnation incarnation,
        long tombstoneSequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKind);
        ArgumentNullException.ThrowIfNull(contract);
        if (tombstoneSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(tombstoneSequence));
        string[] components =
        [
            "hpd.auth.cleanup-work.v1",
            tenantId.ToString("D"),
            subjectKind,
            subjectId.ToString("D"),
            contract.Id,
            contract.Version.ToString(CultureInfo.InvariantCulture),
            contract.ContractChecksum,
            incarnation.ToBase64Url(),
            tombstoneSequence.ToString(CultureInfo.InvariantCulture),
        ];
        return Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\0', components))));
    }
}
