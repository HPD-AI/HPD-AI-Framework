using System.Security.Cryptography;
using System.Text;

namespace HPD.Auth.LegacyImporter;

/// <summary>Strict canonical conversions shared by import and legacy-token validation tests.</summary>
internal static class LegacyValueCodec
{
    private static ReadOnlySpan<byte> RefreshDigestDomain => "hpd.auth.refresh.legacy.v1"u8;

    internal static byte[] DecodeCanonicalBase64(string value, int exactDecodedBytes, string field)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0 || value.Any(char.IsWhiteSpace)) Invalid(field);
        int maximumBytes = checked((value.Length / 4) * 3);
        byte[] buffer = new byte[maximumBytes];
        if (!Convert.TryFromBase64String(value, buffer, out int written)
            || written != exactDecodedBytes
            || !StringComparer.Ordinal.Equals(Convert.ToBase64String(buffer, 0, written), value))
        {
            CryptographicOperations.ZeroMemory(buffer);
            Invalid(field);
        }
        return buffer.AsSpan(0, written).ToArray();
    }

    internal static byte[] ComputeLegacyRefreshDigest(string bearer)
    {
        byte[] token = DecodeCanonicalBase64(bearer, 64, "RefreshTokens.Token");
        try
        {
            byte[] preimage = new byte[RefreshDigestDomain.Length + token.Length];
            RefreshDigestDomain.CopyTo(preimage);
            token.CopyTo(preimage.AsSpan(RefreshDigestDomain.Length));
            try { return SHA256.HashData(preimage); }
            finally { CryptographicOperations.ZeroMemory(preimage); }
        }
        finally { CryptographicOperations.ZeroMemory(token); }
    }

    internal static Guid ParseCanonicalGuid(string value, string field)
    {
        if (!Guid.TryParseExact(value, "D", out Guid result)
            || !StringComparer.Ordinal.Equals(result.ToString("D"), value)) Invalid(field);
        return result;
    }

    internal static DateTimeOffset ParseCanonicalTimestamp(string value, string field)
    {
        if (!DateTimeOffset.TryParseExact(value, "O", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTimeOffset result)) Invalid(field);
        return result.ToUniversalTime();
    }

    internal static bool ReadBoolean(long value, string field) => value switch
    {
        0 => false,
        1 => true,
        _ => throw new LegacyImportException(LegacyImportFailure.SourceSchemaMismatch, $"Legacy field '{field}' is not canonical."),
    };

    private static void Invalid(string field) =>
        throw new LegacyImportException(LegacyImportFailure.SourceSchemaMismatch, $"Legacy field '{field}' is not canonical.");
}
