using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace HPD.Agent.Bots.Discord;

internal static class DiscordSignatureVerifier
{
    public static bool Verify(byte[] bodyBytes, string signatureHex, string timestamp, string publicKeyHex)
    {
        if (string.IsNullOrWhiteSpace(signatureHex) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(publicKeyHex))
        {
            return false;
        }

        try
        {
            var signatureBytes = Convert.FromHexString(signatureHex);
            var publicKeyBytes = Convert.FromHexString(publicKeyHex);
            if (signatureBytes.Length != Ed25519.SignatureSize ||
                publicKeyBytes.Length != Ed25519.PublicKeySize)
            {
                return false;
            }

            var timestampBytes = Encoding.UTF8.GetBytes(timestamp);
            var messageBytes = new byte[timestampBytes.Length + bodyBytes.Length];
            Buffer.BlockCopy(timestampBytes, 0, messageBytes, 0, timestampBytes.Length);
            Buffer.BlockCopy(bodyBytes, 0, messageBytes, timestampBytes.Length, bodyBytes.Length);

            return Ed25519.Verify(
                signatureBytes,
                0,
                publicKeyBytes,
                0,
                messageBytes,
                0,
                messageBytes.Length);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
