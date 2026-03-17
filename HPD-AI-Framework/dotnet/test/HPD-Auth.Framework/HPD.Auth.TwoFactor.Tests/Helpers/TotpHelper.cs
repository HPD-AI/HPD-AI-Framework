namespace HPD.Auth.TwoFactor.Tests.Helpers;

/// <summary>
/// Utility methods for generating real TOTP codes in tests.
/// Uses RFC 6238: HMAC-SHA1, 30-second time step, 6-digit truncation.
/// </summary>
public static class TotpHelper
{
    /// <summary>
    /// Generates a 6-digit TOTP code for the given base32-encoded key
    /// using the current UTC time.
    /// </summary>
    public static string GenerateCode(string base32Key)
    {
        var keyBytes = Base32Decode(base32Key);
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        var stepBytes = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian) Array.Reverse(stepBytes);

        using var hmac = new System.Security.Cryptography.HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(stepBytes);

        var offset = hash[^1] & 0x0F;
        var code = ((hash[offset] & 0x7F) << 24)
                   | (hash[offset + 1] << 16)
                   | (hash[offset + 2] << 8)
                   | hash[offset + 3];

        return (code % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var input = base32.ToUpperInvariant().TrimEnd('=');
        var result = new List<byte>();

        int buffer = 0, bitsLeft = 0;
        foreach (var c in input)
        {
            int val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                result.Add((byte)(buffer >> bitsLeft));
                buffer &= (1 << bitsLeft) - 1;
            }
        }
        return result.ToArray();
    }
}
